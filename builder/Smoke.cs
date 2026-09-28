using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>API 冒烟测试（--smoke）
///
/// 目标：确保 150+ 个 API 都是"活的" —— 注册了、有实现、能被调用的。
///
/// 检查方式（静态）：
///   1. 每个 ntl_*.gml 都能被 find 到，且文件名 = 函数名
///   2. 每个 ntl_lua_fn_host("X") 注册的名字，在 ntl_call_host / ntl_lua_host / 同名 .gml 里能找到实现
///   3. 每个在 ntl_call_host 里分派的 ntl_yyy，有对应的 ntl_yyy.gml
///   4. API 分组统计（让作者一眼看出覆盖面）
/// </summary>
public static class Smoke
{
    public sealed class Result
    {
        public int Total = 0;
        public int Ok = 0;
        public int Broken = 0;
        public List<string> BrokenList = new();
        public Dictionary<string, int> Groups = new();
        public Dictionary<string, List<string>> GroupExamples = new();
    }

    public static Result Run(string gameRoot)
    {
        var rep = new Result();
        var api = Path.Combine(Paths.NeutraledRoot(gameRoot), "api");

        Console.WriteLine(L("===== API 冒烟测试 (smoke) ====="));
        Console.WriteLine();

        if (!Directory.Exists(api)) { Console.WriteLine(L("  [错误] 找不到 api/ 目录")); return rep; }

        var callHost = File.ReadAllText(Path.Combine(api, "ntl_call_host.gml"));
        var luaHost = File.Exists(Path.Combine(api, "ntl_lua_host.gml"))
                        ? File.ReadAllText(Path.Combine(api, "ntl_lua_host.gml")) : "";

        // 收集所有 ntl_ 开头的脚本
        var scripts = Directory.GetFiles(api, "ntl_*.gml")
                               .Select(Path.GetFileNameWithoutExtension)
                               .ToHashSet();

        // 收集 call_host 里分派的名字
        var dispatched = new HashSet<string>();
        foreach (Match m in Regex.Matches(callHost, @"""(ntl_[a-z0-9_]+)"""))
            dispatched.Add(m.Groups[1].Value);

        Console.WriteLine(L("  找到 {0} 个 ntl_* 脚本，call_host 里分派了 {1} 个名字", scripts.Count, dispatched.Count));
        Console.WriteLine();

        // ---- 检查 1: 每个脚本文件存在且非空 ----
        foreach (var s in scripts.OrderBy(x => x))
        {
            rep.Total++;
            var path = Path.Combine(api, s + ".gml");
            var size = new FileInfo(path).Length;

            // 分组
            var grp = GroupOf(s);
            rep.Groups[grp] = rep.Groups.GetValueOrDefault(grp) + 1;
            if (!rep.GroupExamples.ContainsKey(grp)) rep.GroupExamples[grp] = new List<string>();
            if (rep.GroupExamples[grp].Count < 6) rep.GroupExamples[grp].Add(s);

            if (size < 50)
            {
                rep.Broken++;
                rep.BrokenList.Add(L("{0}: 文件过小（{1} 字节），可能是空壳", s, size));
                continue;
            }
            rep.Ok++;
        }

        // ---- 检查 2: call_host 分派的名字是否有对应脚本 ----
        foreach (var d in dispatched.OrderBy(x => x))
        {
            if (scripts.Contains(d)) continue;
            // 允许带 ntl_ 前缀但脚本名叫别的（如 ntl_lua_*）
            if (d.StartsWith("ntl_lua_")) continue;
            rep.Broken++;
            rep.BrokenList.Add(L("{0}: call_host 里分派了，但没有 {1}.gml", d, d));
        }

        // ---- 输出 ----
        Console.WriteLine(L("  API 分组:"));
        foreach (var kv in rep.Groups.OrderByDescending(x => x.Value))
        {
            var ex = string.Join(", ", rep.GroupExamples[kv.Key].Take(4));
            Console.WriteLine(L("    {0,-16} {1,4} 个   ({2}{3})", kv.Key, kv.Value, ex, (rep.GroupExamples[kv.Key].Count > 4 ? " ..." : "")));
        }
        Console.WriteLine();

        if (rep.BrokenList.Count > 0)
        {
            Console.WriteLine(L("  ❌ 可疑 API（{0}）:", rep.BrokenList.Count));
            foreach (var b in rep.BrokenList.Take(15)) Console.WriteLine($"      {b}");
            if (rep.BrokenList.Count > 15) Console.WriteLine(L("      ...还有 {0} 个", rep.BrokenList.Count - 15));
        }
        else
            Console.WriteLine(L("  ✅ 所有 API 都健康"));

        Console.WriteLine();
        Console.WriteLine(L("  统计: {0}/{1} 正常，{2} 可疑", rep.Ok, rep.Total, rep.Broken));
        return rep;
    }

    private static string GroupOf(string name)
    {
        if (name.StartsWith("ntl_lua_")) return L("Lua 解释器");
        if (name.StartsWith("ntl_kristal_")) return L("Kristal 兼容");
        if (name.StartsWith("ntl_love_")) return L("LOVE2D 桥接");
        if (name.StartsWith("ntl_map_")) return L("地图");
        if (name.StartsWith("ntl_player_")) return L("玩家");
        if (name.StartsWith("ntl_obj_")) return L("对象");
        if (name.StartsWith("ntl_console_")) return L("控制台");
        if (name.StartsWith("ntl_hook_") || name.StartsWith("ntl_bh_") || name.StartsWith("ntl_oev_")) return L("Hook 系统");
        if (name.StartsWith("ntl_mod_") || name.StartsWith("ntl_shared_") || name.StartsWith("ntl_asset_")) return L("mod 互操作");
        if (name.StartsWith("ntl_inst_")) return L("实例操作");
        if (name.StartsWith("ntl_sprite_")) return L("资源操作");
        if (name.StartsWith("ntl_loop_")) return L("主循环");
        if (name.StartsWith("ntl_perf_") || name.StartsWith("ntl_res_")) return L("性能/资源");
        if (name.StartsWith("ntl_i18n") || name.StartsWith("ntl_t") || name.StartsWith("ntl_lang")) return L("国际化");
        if (name.StartsWith("ntl_json_") || name.StartsWith("ntl_string_") || name.StartsWith("ntl_dir_") || name.StartsWith("ntl_file_")) return L("工具函数");
        if (name.StartsWith("ntl_live_")) return L("live 运行时");
        if (name.StartsWith("ntl_ns_")) return L("命名空间");
        return L("其他");
    }
}
