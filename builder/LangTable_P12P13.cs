using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P1-2 / P1-3：统一搜索（--search / --gb-search）与统一下载+导入
/// （--install-id / --install-url / --route-probe / 旧名别名）的中英对照。
/// 规则同其它 LangTable：key 是 L() 里的中文原文，value 只允许 ASCII，占位符必须与 key 一致。</summary>
public static class LangTable_P12P13
{
    public static void Fill(Dictionary<string, string> d)
    {
        // ---- P1-2 搜索 ----
        d["搜索计划（dry-run，零网络请求）:"] = "Search plan (dry-run, zero network requests):";
        d["  查询: {0}"] = "  query: {0}";
        d["  每页: {0}"] = "  per page: {0}";
        d["  黑名单: {0}"] = "  blocklist: {0}";
        d["已启用（过滤命中项）"] = "enabled (filter hits)";
        d["已禁用（--no-blocklist）"] = "disabled (--no-blocklist)";
        d["  URL: {0}"] = "  URL: {0}";
        d["  请求参数: {0}"] = "  request params: {0}";
        d["  结果: 未请求（--dry-run）"] = "  results: not requested (--dry-run)";
        d["[GB] 搜索「{0}」（每页 {1} 条）"] = "[GB] searching \"{0}\" ({1} per page)";
        d["[GB] 黑名单已过滤 {0} 条"] = "[GB] blocklist filtered out {0} result(s)";
        d["[GB] 没有搜索结果"] = "[GB] no results";
        d["[GB] {0} | {1} | {2} | {3} | {4} | {5}"] = "[GB] {0} | {1} | {2} | {3} | {4} | {5}";
        d["[GB] 共 {0} 条结果"] = "[GB] {0} result(s) in total";
        d["[GB] 搜索失败: {0}"] = "[GB] search failed: {0}";
        d["[deprecated] {0} 是旧名，请改用 {1}（语义相同）"] = "[deprecated] {0} is a legacy name; use {1} instead (same behavior)";
        d["[deprecated] {0} 是旧名，请改用 {1}（{2}）"] = "[deprecated] {0} is a legacy name; use {1} instead ({2})";
        d["缺少搜索关键词（用法: {0} <关键词> [--per-page N] [--no-blocklist] [--dry-run] [--json]）"] = "missing search query (usage: {0} <query> [--per-page N] [--no-blocklist] [--dry-run] [--json])";
        d["--per-page 必须在 {0}..{1} 之间（收到 {2}）"] = "--per-page must be between {0} and {1} (got {2})";

        // ---- P1-3 下载 + 导入 ----
        d["[错误] --as 的取值非法: {0}（可用 {1}）"] = "[error] invalid --as value: {0} (valid: {1})";
        d["[错误] --file 必须是正整数（收到 {0}）"] = "[error] --file must be a positive integer (got {0})";
        d["[错误] --install-url 必须是 http(s) 地址（收到 {0}）"] = "[error] --install-url must be an http(s) URL (got {0})";
        d["[错误] --install-id 必须是正整数（收到 {0}）"] = "[error] --install-id must be a positive integer (got {0})";
        d["安装计划（dry-run，零网络请求）:"] = "Install plan (dry-run, zero network requests):";
        d["  来源: {0}"] = "  source: {0}";
        d["  文件名: {0}"] = "  file name: {0}";
        d["  下载目标: {0}"] = "  download target: {0}";
        d["  来源: GameBanana #{0}"] = "  source: GameBanana #{0}";
        d["  文件列表 URL: {0}"] = "  file list URL: {0}";
        d["  文件: 第 {0} 个（1 起）"] = "  file: #{0} (1-based)";
        d["  文件: {0}"] = "  file: {0}";
        d["默认（最大文件）"] = "default (largest file)";
        d["默认（第一个）"] = "default (first file)";
        d["  强制包型: {0}（{1}）"] = "  forced kind: {0} ({1})";
        d["  章节: {0}"] = "  chapter: {0}";
        d["  1. 探测: 下载后按包内容探测"] = "  1. probe: inspect package contents after download";
        d["  2. 下载: {0}"] = "  2. download: {0}";
        d["从上面的文件列表运行时选择"] = "chosen at runtime from the file list above";
        d["  3. 导入: {0}"] = "  3. import: {0}";
        d["探测后自动选择导入器"] = "importer selected automatically after probing";
        d["{0} 导入器"] = "{0} importer";
        d["  零网络请求：未访问 GameBanana API。"] = "  zero network requests: the GameBanana API was not contacted.";
        d["[错误] 类型名非法: {0}（只允许字母，最长 16 位）"] = "[error] invalid type name: {0} (letters only, max 16 chars)";
        d["[错误] 该 GameBanana 条目没有可下载文件（#{0}）"] = "[error] this GameBanana entry has no downloadable files (#{0})";
        d["[错误] --file 序号超出范围（该条目共 {0} 个文件）"] = "[error] --file index out of range (the entry has {0} files)";
        d["[错误] 文件 {0} 没有下载地址"] = "[error] file {0} has no download URL";
        d["[安装] 选中文件: {0}（id {1}，{2} KB）"] = "[install] selected file: {0} (id {1}, {2} KB)";
        d["[错误] 下载失败: {0}"] = "[error] download failed: {0}";
        d["[安装] 下载: {0}"] = "[install] downloading: {0}";
        d["[安装] 探测结果: {0}（{1}）"] = "[install] probe result: {0} ({1})";
        d["[安装] 导入器: {0}"] = "[install] importer: {0}";
        d["[安装] 包型未识别，交给传统导入器尝试"] = "[install] package kind not recognized; falling back to the classic importer";
        d["[安装] 完成: {0}"] = "[install] done: {0}";
        d["[安装] 失败（返回码 {0}）: {1}"] = "[install] failed (exit code {0}): {1}";
        d["[错误] 安装失败: {0}"] = "[error] install failed: {0}";

        // ---- P1-3 包探测 ----
        d["[错误] 缺少路径（用法: --route-probe <路径> [--as {0}] [--json]）"] = "[error] missing path (usage: --route-probe <path> [--as {0}] [--json])";
        d["[错误] 找不到路径: {0}"] = "[error] path not found: {0}";
        d["包探测: {0}"] = "Package probe: {0}";
        d["  包型: {0}"] = "  kind: {0}";
        d["  建议导入器: {0}"] = "  recommended importer: {0}";
        d["  理由: {0}"] = "  reason: {0}";
        d["  强制: 是（--as {0}）"] = "  forced: yes (--as {0})";
        d["目录里有 mod.lua（Kristal 工程）"] = "directory contains mod.lua (Kristal project)";
        d["mod.json 声明了 engineVer（Kristal mod）"] = "mod.json declares engineVer (Kristal mod)";
        d["目录是成品程序（exe + 运行库，无 mod 签名）"] = "directory is a standalone build (exe + runtime DLLs, no mod signature)";
        d["目录里有 mod.json（Neutraled / 传统 mod 包）"] = "directory contains mod.json (Neutraled / classic mod package)";
        d["目录里有 .xdelta 补丁"] = "directory contains an .xdelta patch";
        d["目录里只有 data.win（修改版数据文件）"] = "directory contains only data.win (modified data file)";
        d["目录里有 data.win 及其它负载（GM3P 整包）"] = "directory contains data.win plus other payload (GM3P whole package)";
        d["文件以 xdelta 魔数开头"] = "file starts with the xdelta magic";
        d["文件扩展名是 .xdelta"] = "file extension is .xdelta";
        d["文件头是 GameMaker FORM/GEN8"] = "file header is GameMaker FORM/GEN8";
        d["文件名是 data.win"] = "file name is data.win";
        d["zip 内有 mod.lua（Kristal）"] = "zip contains mod.lua (Kristal)";
        d["zip 内的 mod.json 声明了 engineVer（Kristal）"] = "zip's mod.json declares engineVer (Kristal)";
        d["zip 内有 exe 与运行库（外部引擎包）"] = "zip contains an exe and runtime DLLs (external engine package)";
        d["zip 内有 .xdelta 补丁"] = "zip contains an .xdelta patch";
        d["zip 内只有 data.win（修改版数据文件）"] = "zip contains only data.win (modified data file)";
        d["zip 内有 data.win 及其它文件（GM3P 整包）"] = "zip contains data.win plus other files (GM3P whole package)";
        d["zip 内有 mod.json（Neutraled mod 包）"] = "zip contains mod.json (Neutraled mod package)";
        d["按扩展名判定为 .ntlmod 包"] = "treated as an .ntlmod package by extension";
        d["可执行文件（外部引擎包）"] = "executable file (external engine package)";
        d["Lua 文件（Kristal 脚本）"] = "Lua file (Kristal script)";
        d["该压缩格式无法在不下载/解压时探测（可用 --as 指定）"] = "this archive format cannot be probed without downloading/extracting (use --as)";
        d["由 --as 强制指定"] = "forced by --as";
        d["无法识别的包（可用 --as 指定）"] = "unrecognized package (use --as)";

        // ---- Program.cs 参数解析 ----
        d["[错误] --per-page 需要整数: {0}"] = "[error] --per-page requires an integer: {0}";
        d["[错误] --file 需要整数: {0}"] = "[error] --file requires an integer: {0}";

        // ---- PrintHelp ----
        d["  --install-id <gamebananaId> [--file N] [--as-delta] [--dry-run] [--json]   统一下载 + 导入（自动探测包型）"] = "  --install-id <gamebananaId> [--file N] [--as-delta] [--dry-run] [--json]   unified download + import (auto package probe)";
        d["  --install-url <url> [--dry-run] [--json]   从直链下载并自动导入"] = "  --install-url <url> [--dry-run] [--json]   download from a direct URL and import automatically";
        d["  --route-probe <路径|目录> [--as 包型] [--json]   只读探测包型与建议导入器"] = "  --route-probe <path|dir> [--as kind] [--json]   read-only package probe with importer recommendation";
        d["      --as xdelta|datawin|ntlmod|kristal|gm3p|external   强制包型（探测失败或误判时用）"] = "      --as xdelta|datawin|ntlmod|kristal|gm3p|external   force the package kind (when probing fails or misjudges)";
        d["  --fetch <id> / --gb-install <id>   旧名（已弃用）：改用 --install-id"] = "  --fetch <id> / --gb-install <id>   legacy names (deprecated): use --install-id";
        d["  --search <关键词> [--per-page N] [--no-blocklist] [--json]   统一搜索（默认过滤黑名单；--dry-run 零网络）"] = "  --search <query> [--per-page N] [--no-blocklist] [--json]   unified search (blocklist on by default; --dry-run uses no network)";
        d["  --gb-search <关键词>            旧名（已弃用）：改用 --search"] = "  --gb-search <query>            legacy name (deprecated): use --search";
        d["  --gb-install <modId> [--file N] [--chapter 章节] [--force]   旧名（已弃用）：改用 --install-id"] = "  --gb-install <modId> [--file N] [--chapter chapter] [--force]   legacy name (deprecated): use --install-id";
    }
}
