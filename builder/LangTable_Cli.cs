using System.Collections.Generic;

namespace Neutraled.Builder;

/// <summary>英文文案（C4 P1-1：命令行目录 / --help 分层 / --help --audit / --gen-docs / lint 规则 18）。
/// 键 = CliHelp.cs 与 LintRules6.cs 里 L("…") 的中文原文（含前导空格，逐字一致）。
/// 由本轮改造新增，人工整理；新增 --help 文案时同步在这里加英文，否则英文模式会显示中文。</summary>
public static class LangTable_Cli
{
    public static void Fill(Dictionary<string, string> d)
    {
        d["  更多开关: --help all（全部 {0} 个，按类别） / --help advanced / --help dev / --help <开关> / --help --audit"] =
            "  More switches: --help all ({0} total, by category) / --help advanced / --help dev / --help <switch> / --help --audit";
        d[" · 高级"] = " \u00b7 advanced";
        d[" · 开发"] = " \u00b7 dev";
        d["  （需要取值）"] = "  (takes a value)";
        d["[错误] 没有这个开关: {0}"] = "[error] no such switch: {0}";
        d["  你是不是想输入: {0}"] = "  did you mean: {0}";
        d["  查看全部开关: --help all"] = "  see all switches: --help all";
        d["Neutraled 命令行开关（共 {0} 个；★ = 需要取值）"] = "Neutraled command-line switches ({0} total; * = takes a value)";
        d["  --help           场景化帮助（推荐先看）"] = "  --help           scenario-based help (start here)";
        d["  --help <开关>    单个开关说明"] = "  --help <switch>  details for one switch";
        d["  更全: --help all（含开发/内部开关）"] = "  More complete: --help all (includes dev/internal switches)";
        d["===== 命令行目录审计 ====="] = "===== CLI catalog audit =====";
        d["  CliCatalog.Known: {0} 个    CliCatalogDetails: {1} 条"] = "  CliCatalog.Known: {0}    CliCatalogDetails: {1} entries";
        d["  [通过] 没有「合法但没说明」的开关"] = "  [ok] no legal switch is missing a description";
        d["  [缺] {0} 个: {1}"] = "  [missing] {0}: {1}";
        d["  [通过] 没有「有说明但不合法」的开关"] = "  [ok] no described switch is illegal";
        d["  [多] {0} 个: {1}"] = "  [extra] {0}: {1}";
        d["  [重复] {0}: {1}"] = "  [duplicate] {0}: {1}";
        d["  [类别非法] {0}: {1}"] = "  [bad group] {0}: {1}";
        d["结果: 目录完整 ✓"] = "Result: catalog complete ✓";
        d["结果: 目录不完整（先补齐 CliCatalogDetails）"] = "Result: catalog incomplete (fill in CliCatalogDetails first)";
        d["[过期] 文档不存在: {0}（先跑 --gen-docs）"] = "[stale] document does not exist: {0} (run --gen-docs first)";
        d["[过期] 文档与开关目录不一致: {0}（重新跑 --gen-docs）"] = "[stale] document does not match the switch catalog: {0} (re-run --gen-docs)";
        d["[通过] 文档与开关目录一致: {0}"] = "[ok] document matches the switch catalog: {0}";
        d["已生成: {0}（{1} 个开关）"] = "Generated: {0} ({1} switches)";
        d["常用入口: `--deploy` / `--deploy-all` / `--launch root|chapterN|all` / `--help` / `--verify` / `--doctor` / `--deploy-state` / `--restore-session`。"] =
            "Common entry points: `--deploy` / `--deploy-all` / `--launch root|chapterN|all` / `--help` / `--verify` / `--doctor` / `--deploy-state` / `--restore-session`.";
        d["常用"] = "common";
        d["进阶"] = "advanced";
        d["开发"] = "dev";
        d["是"] = "yes";
        // LintRules6（规则 18）
        d["命令行目录缺条目"] = "CLI catalog missing entry";
        d["命令行目录多条目"] = "CLI catalog extra entry";
        d["命令行目录重复条目"] = "CLI catalog duplicate entry";
        d["开关 {0} 在 CliCatalog.Known 里，但 CliCatalogDetails 没有对应条目（--help all 里看不到它）"] =
            "switch {0} is in CliCatalog.Known but CliCatalogDetails has no entry for it (invisible in --help all)";
        d["开关 {0} 有目录条目，但不在 CliCatalog.Known（用户按它输入会被判未知开关）"] =
            "switch {0} has a catalog entry but is not in CliCatalog.Known (typing it is rejected as unknown)";
        d["开关 {0} 在 CliCatalogDetails 里登记了多次"] = "switch {0} is listed more than once in CliCatalogDetails";
    }
}
