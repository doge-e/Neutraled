/// ntl_is_root() —— 当前进程是不是"顶层章节选择"的 root 进程？
/// 判据（按可靠性排序）：
///   ① 部署时写入的产物清单 <working_directory>Neutraled/scope.json 的 target == "root"
///      （builder 写 mods.json 时同目录一并写；这是唯一不靠"猜"的来源）
///   ② 归一化后 working_directory == program_directory ⇒ 本进程跑的就是游戏根产物
///   ⚠ 旧判据「working_directory 里不含子串 chapter」有两个洞（2026-09-26 审计）：
///     a) 安装路径本身含 "chapter"（例 E:\games\chapter_test\DELTARUNE）→ root 被判成章节进程
///        （选择器界面完全不画、autoskip 不跑）；
///     b) 产物目录名不含 "chapter"（独立章/时间线，如 ntl_timeline_4_test_timelineforest_forest）
///        → 章节进程被判成 root → ntl_autoskip 会把玩家**弹去官方章节**（ntl_autoskip.gml:54-59）。
///   所以路径只作次选，且判据改成"与 program_directory 相等"而不是子串。
if (!variable_global_exists("ntl_is_root_cache"))
{
    var _sc = ntl_product_scope();
    if (_sc != "") global.ntl_is_root_cache = (_sc == "root") ? 1 : 0;
    else
    {
        var _wd = string_lower(string_replace_all(string(working_directory), "/", "\\"));
        var _pd = string_lower(string_replace_all(string(program_directory), "/", "\\"));
        while (string_length(_wd) > 1 && string_char_at(_wd, string_length(_wd)) == "\\") _wd = string_delete(_wd, string_length(_wd), 1);
        while (string_length(_pd) > 1 && string_char_at(_pd, string_length(_pd)) == "\\") _pd = string_delete(_pd, string_length(_pd), 1);
        global.ntl_is_root_cache = (_wd == _pd) ? 1 : 0;
    }
}
return global.ntl_is_root_cache;
