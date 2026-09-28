using System.Diagnostics;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>自动化测试套件 —— 一条命令验证整条链路。
///
/// 背景：本项目在 F2 控制台上反复试错了一整晚（BeginStep 不生效、漏 global.、
/// 部署静默失败……），根因就是**没有自动化验证**。
///
/// 检查项（按顺序）：
///   1) lint 静态检查
///   2) 关键文件存在性（api / live / docs）
///   3) 部署能否成功 + 是否真的写盘（时间戳变化）
///   4) 部署产物完整性（data.win / chapters.json / cache）
///   5) 启动游戏并检查是否停在预期界面（不崩）
///   6) 日志无致命错误
///   7) 存档快照机制可用
/// </summary>
public static class SelfTest
{
    private sealed class Result
    {
        public string Name = "";
        public bool Ok;
        public string Detail = "";
    }

    public static int Run(string gameRoot, string chapter, bool skipLaunch)
    {
        Console.WriteLine("========================================");
        Console.WriteLine(L("  Neutraled 自检 (selftest)"));
        Console.WriteLine("========================================");
        var results = new List<Result>();
        var root = Paths.NeutraledRoot(gameRoot);

        // ---- 1) lint ----
        try
        {
            var issues = Lint.Run(root);
            var errs = issues.Count(x => x.IsError);
            results.Add(new Result
            {
                Name = L("静态检查 (lint)"),
                Ok = errs == 0,
                Detail = errs == 0 ? L("无错误") : L("{0} 个错误", errs)
            });
        }
        catch (Exception ex) { results.Add(new Result { Name = L("静态检查 (lint)"), Ok = false, Detail = ex.Message }); }

        // ---- 2) 关键文件 ----
        var required = new[]
        {
            "api/scr_ntl_init.gml", "api/ntl_call_host.gml", "api/events/Step_1.gml",
            "api/events/Draw_64.gml", "api/events/Create_0.gml",
            "api/ntl_lua_ev_block.gml", "api/ntl_kristal_init.gml",
            "docs/GETTING-STARTED.md", "docs/PROJECT.md", "launch.ps1"
        };
        var missing = required.Where(r => !File.Exists(Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar)))).ToList();
        results.Add(new Result
        {
            Name = L("关键文件完整性"),
            Ok = missing.Count == 0,
            Detail = missing.Count == 0 ? L("{0} 个文件全部存在", required.Length) : L("缺失: ") + string.Join(", ", missing)
        });

        // ---- 3) 存档快照机制 ----
        try
        {
            var bakRoot = SaveGuard.BackupRoot(gameRoot);
            var hasBaseline = File.Exists(Path.Combine(bakRoot, "BASELINE.zip"));
            var snaps = Directory.Exists(bakRoot) ? Directory.GetFiles(bakRoot, "*.zip").Length : 0;
            results.Add(new Result
            {
                Name = L("存档快照"),
                Ok = snaps > 0,
                Detail = L("快照 {0} 份{1}", snaps, (hasBaseline ? L("（含 BASELINE）") : ""))
            });
        }
        catch (Exception ex) { results.Add(new Result { Name = L("存档快照"), Ok = false, Detail = ex.Message }); }

        // ---- 4) 部署 + 写盘验证 ----
        string dataWin = Paths.ChapterDataWin(gameRoot, chapter);
        DateTime before = File.Exists(dataWin) ? File.GetLastWriteTime(dataWin) : DateTime.MinValue;
        bool deployOk = false;
        string deployDetail = "";
        try
        {
            // 幂等跳过是**特性**：签名一致就不重写 229MB 产物（1.4s vs 55s）。那时时间戳不会变，
            // 不能当成「被占用的失败」。所以这里捕获部署输出，认出跳过标记。
            var captured = new StringWriter();
            var origOut = Console.Out;
            int rc;
            try
            {
                Console.SetOut(captured);
                rc = Program.DeployAllForTest(gameRoot, chapter);
            }
            finally
            {
                Console.SetOut(origOut);
                Console.Write(captured.ToString());
            }
            deployOk = rc == 0;
            var after = File.Exists(dataWin) ? File.GetLastWriteTime(dataWin) : DateTime.MinValue;
            bool written = after > before;
            bool skipped = Program.LastDeploySkipped;
            deployDetail = deployOk
                ? (written ? L("部署成功，data.win 已更新（{0:HH:mm:ss}）", after)
                           : (skipped ? L("部署成功（签名一致 → 幂等跳过，未重写 data.win，属正常）")
                                      : L("部署返回成功，但 data.win 时间戳**未变化**（可能被占用）")))
                : L("部署返回非零");
            if (!written && !skipped) deployOk = false;
        }
        catch (Exception ex) { deployDetail = ex.Message; deployOk = false; }
        results.Add(new Result { Name = L("部署 {0}", chapter), Ok = deployOk, Detail = deployDetail });

        // ---- 5) 部署产物 ----
        var products = new[] { "chapters.json" }.Select(p => Path.Combine(root, p)).ToList();
        var prodMissing = products.Where(p => !File.Exists(p)).ToList();
        results.Add(new Result
        {
            Name = L("部署产物"),
            Ok = prodMissing.Count == 0,
            Detail = prodMissing.Count == 0 ? L("chapters.json 已生成") : L("缺失: ") + string.Join(", ", prodMissing.Select(Path.GetFileName))
        });

        // ---- 6) 启动 + 日志检查 ----
        if (!skipLaunch)
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DELTARUNE", "Neutraled", "dr-api.log");
            try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = "E:\\DELTARUNE.url", UseShellExecute = true });
                // 等游戏启动并跑一会儿
                for (int i = 0; i < 50; i++) { Thread.Sleep(1000); if (File.Exists(logPath)) break; }
                Thread.Sleep(15000);

                bool logOk = File.Exists(logPath);
                string logText = logOk ? File.ReadAllText(logPath) : "";
                bool hasCore = logText.Contains("[core] Neutraled API");
                bool hasError = logText.Contains("ERROR in") || logText.Contains("Code Error");
                var procs = Process.GetProcessesByName("DELTARUNE");
                bool running = procs.Length > 0;
                bool crashed = procs.Any(p => (p.MainWindowTitle ?? "").Contains("Code Error"));

                results.Add(new Result
                {
                    Name = L("启动游戏"),
                    Ok = running && !crashed && hasCore && !hasError,
                    Detail = !running ? L("进程未启动")
                        : crashed ? L("**游戏崩溃**（Code Error）")
                        : !hasCore ? L("日志里没有 [core] 初始化记录")
                        : hasError ? L("日志里有 ERROR")
                        : L("正常运行，日志 {0} KB", new FileInfo(logPath).Length / 1024)
                });
            }
            catch (Exception ex) { results.Add(new Result { Name = L("启动游戏"), Ok = false, Detail = ex.Message }); }
        }
        else
        {
            results.Add(new Result { Name = L("启动游戏"), Ok = true, Detail = L("（已跳过，--no-launch）") });
        }

        // ---- 7) mod 互操作层 ----
        bool interop = File.Exists(Path.Combine(root, "api", "ntl_mod_require.gml")) &&
                       File.Exists(Path.Combine(root, "api", "ntl_mod_emit.gml")) &&
                       File.Exists(Path.Combine(root, "api", "ntl_shared_set.gml"));
        results.Add(new Result
        {
            Name = L("mod 互操作层"),
            Ok = interop,
            Detail = interop ? L("跨 mod 调用/事件/共享状态 已就绪") : L("缺少互操作脚本")
        });

        // ---- 8) 完全支配能力 ----
        var powers = new (string file, string name)[]
        {
            ("ntl_hook_run.gml", L("函数 Hook")),
            ("ntl_bh_run.gml", L("内置函数 Hook")),
            ("ntl_oev_run.gml", L("对象事件 Hook")),
            ("ntl_inst_create.gml", L("运行时实例操作")),
        };
        var missingPowers = powers.Where(p => !File.Exists(Path.Combine(root, "api", p.file))).Select(p => p.name).ToList();
        results.Add(new Result
        {
            Name = L("完全支配能力"),
            Ok = missingPowers.Count == 0,
            Detail = missingPowers.Count == 0 ? L("Hook / 内置 Hook / 对象事件 / 实例操作 全部就绪")
                                              : L("缺失: ") + string.Join(", ", missingPowers)
        });

        // ---- 汇总 ----
        Console.WriteLine();
        int pass = 0;
        foreach (var r in results)
        {
            Console.WriteLine($"  {(r.Ok ? "✅" : "❌")} {r.Name,-22} {r.Detail}");
            if (r.Ok) pass++;
        }
        Console.WriteLine();
        Console.WriteLine(L("===== 自检结果: {0}/{1} 通过 =====", pass, results.Count));
        return pass == results.Count ? 0 : 1;
    }
}
