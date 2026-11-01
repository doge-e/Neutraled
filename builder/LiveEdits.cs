using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>live 文件保护（不变量 I5：外部修改不丢）。
///
/// 部署 / 缓存应用 / --restore-chapter 都会覆盖 <gameRoot>\data.win 与 chapterN_windows\data.win。
/// 如果当前 live 既不是「上次部署记录的产物指纹」也不是「基线账本里的原版指纹」，说明它在上次部署后
/// 被外部改过（典型：玩家用地图编辑器直接编辑 live 产物；或手动替换过 data.win）：
/// 覆盖前先归档到 Neutraled\live-edits\&lt;slot&gt;\&lt;时间戳&gt;-&lt;sha16&gt;.win（+ .json 元数据），
/// 这样「部署一次就把玩家改动抹掉」不再是静默损失（A3 审计：--launch 命中缓存会隐式写 live）。
///
/// 归档失败不阻断部署（安全方向：不能因为备份目录写不进去就卡住启动），但会响亮打印。
/// 同一内容只归档一次（按 sha16 去重），反复 --force 不会堆满磁盘。</summary>
public static class LiveEdits
{
    public const string DirName = "live-edits";

    /// <summary>覆盖 live 之前调用：live 与记录不一致时归档它。返回是否真的归档了。</summary>
    public static bool ArchiveIfEdited(string gameRoot, string chapter, string liveWin, string reason)
    {
        try
        {
            if (!File.Exists(liveWin)) return false;
            var idn = Baseline.Identify(liveWin);
            if (idn == null) return false;
            var id = idn.Value;

            var st = GameUpdate.Load(gameRoot);
            var rec = st.Slots.FirstOrDefault(x => string.Equals(x.Slot, chapter, StringComparison.OrdinalIgnoreCase));
            var expectedProduct = rec?.ProductFp ?? "";
            var expectedVanilla = Baseline.TryGetFp(gameRoot, chapter) ?? rec?.VanillaFp ?? "";
            // 没有任何期望值（全新安装第一次部署）：无法判断「谁改的」，不归档。
            if (expectedProduct.Length == 0 && expectedVanilla.Length == 0) return false;
            // 正常情况：live == 上次部署的产物 / live == 原版基线 ⇒ 覆盖它不会丢失任何东西。
            if (string.Equals(id.Fp, expectedProduct, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(id.Fp, expectedVanilla, StringComparison.OrdinalIgnoreCase)) return false;

            var dir = Path.Combine(Paths.NeutraledRoot(gameRoot), DirName, chapter);
            var shortSha = id.Sha256.Length >= 16 ? id.Sha256[..16] : id.Sha256;
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "*-" + shortSha + ".win").Length > 0) return false;
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dst = Path.Combine(dir, stamp + "-" + shortSha + ".win");
            File.Copy(liveWin, dst, false);

            var meta = new
            {
                slot = chapter,
                fp = id.Fp,
                sha256 = id.Sha256,
                size = id.Size,
                hasProductMarkers = id.HasMarkers,
                mtimeUtc = File.GetLastWriteTimeUtc(liveWin).ToString("o"),
                archivedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                reason,
                expectedProductFp = expectedProduct,
                expectedVanillaFp = expectedVanilla,
                livePath = liveWin
            };
            Paths.SafeWrite(dst + ".json", JsonSerializer.Serialize(meta, new JsonSerializerOptions(Paths.Json) { WriteIndented = true }));
            Console.WriteLine(L("  [归档] {0}: 游戏文件在上次部署后被修改（{1}）→ 已保存 {2}", chapter, id.Fp, dst));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("  [警告] 归档 live 修改失败（不阻断，继续 {0}）: {1}", reason, ex.Message));
            return false;
        }
    }
}
