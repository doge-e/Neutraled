using System.Collections.Generic;

namespace Neutraled.Builder;

/// <summary>C2 英文表：部署只写 live / live 修改归档 / 单一备份写者（Baseline.AdoptLiveAsBaseline、LiveEdits）。
/// 中文 key 来自 LiveEdits.cs / Baseline.cs / GameUpdate.cs / Installer.cs。改代码里的文案时同步改这里。</summary>
public static class LangTable_C2
{
    public static void Fill(Dictionary<string, string> d)
    {
        d["  [警告] {0}: 原版基线校验未通过，跳过建立备份"] = "  [Warn] {0}: vanilla baseline verification failed; skipping backup creation";
        d["  [归档] {0}: 游戏文件在上次部署后被修改（{1}）→ 已保存 {2}"] = "  [Archived] {0}: game file was modified after the last deploy ({1}) -> saved {2}";
        d["  [警告] 归档 live 修改失败（不阻断，继续 {0}）: {1}"] = "  [Warn] failed to archive live edit (not blocking, continuing {0}): {1}";
        d["  [错误] {0}: 找不到 live data.win，无法采纳（{1}）"] = "  [Error] {0}: no live data.win found; cannot adopt ({1})";
        d["  [错误] {0}: live data.win 读取失败，无法采纳（{1}）"] = "  [Error] {0}: failed to read live data.win; cannot adopt ({1})";
        d["  [错误] {0}: live 含产物标记（已部署），拒绝采纳为原版"] = "  [Error] {0}: live contains product markers (already deployed); refusing to adopt as vanilla";
        d["  [提示] {0}: 旧备份已归档 -> {1}"] = "  [Info] {0}: old backups archived -> {1}";
        d["  [错误] {0}: 归档旧备份失败 {1} -> {2}: {3}"] = "  [Error] {0}: failed to archive old backups {1} -> {2}: {3}";
        d["  [错误] {0}: 采纳后校验未通过：{1}"] = "  [Error] {0}: verification failed after adoption: {1}";
        d["  原版备份: {0} 个槽位已确认（两处备份 + 只读保护，基线账本已同步）"] = "  Vanilla backup: {0} slot(s) confirmed (two backup roots + read-only protection, baseline ledger synced)";
    }
}
