/// ntl_product_scope() —— 本进程所在产物的「部署目标名」：root / chapter1..5 / 独立章（时间线的 srcChapter）
/// 来源：部署时由 builder 写进**每个产物目录**的 <working_directory>Neutraled/scope.json：
///       { "version": 1, "target": "chapter1" }（builder/Program.cs 写 mods.json 的同一处）
///
/// ⚠ 为什么必须靠文件而不是靠猜（2026-09-26 审计 + 真机日志）：
///   - 所有产物共用同一个 exe ⇒ program_directory 恒为游戏根，只有 working_directory 能区分产物；
///   - 旧代码按「路径里第一个 chapter 后面的数字」猜作用域，时间线产物目录名
///     ntl_timeline_9_ntl_chapter_c3a8e36c4_c3a8e36c4 里 chapter 后面是下划线 → 猜不出来 →
///     退回 config.auto_chapter，而 auto_chapter 从不随产物更新 ⇒ 载入别的产物的 mod 脚本
///     （桥函数不在本产物 → [live] [错误] 未知函数: <slug>_hooks），auto_chapter=0 时则一个都不载入；
///   - 同一份清单也让 ntl_is_root() 不再依赖「路径里含不含 chapter」这种子串判据。
///
/// 读不到（外部章节 exe / 旧部署产物）返回 ""，调用方自行兜底。
if (!variable_global_exists("ntl_scope_cache"))
{
    global.ntl_scope_cache = "";
    var _p = string(working_directory) + "Neutraled/scope.json";
    if (file_exists(_p))
    {
        try
        {
            var _j = json_parse(ntl_live_file_read(_p));
            if (_j != undefined && variable_struct_exists(_j, "target"))
            {
                var _t = variable_struct_get(_j, "target");
                if (is_string(_t) && string_length(_t) > 0) global.ntl_scope_cache = string_lower(_t);
            }
        }
        catch (e) { global.ntl_scope_cache = ""; }
    }
}
return global.ntl_scope_cache;
