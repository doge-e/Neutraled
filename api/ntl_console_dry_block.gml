/// ntl_console_dry_block(cmd, detail) —— 破坏性命令的统一闸门
///   调用方式（放在"真正改状态"那一步之前）：
///       if (ntl_console_dry_block("warp", "跳到房间 " + _rn)) return 0;
///   返回：1 = 当前是干跑状态（已打印预览，调用者必须直接返回、不要执行）
///         0 = 正常状态（调用者继续执行）
///   注意：参数校验要放在本调用之前 —— 干跑时也要能查出"房间不存在"这类错误。
var _cmd = string(argument[0]);
var _detail = (argument_count > 1) ? string(argument[1]) : "";
if (!variable_global_exists("ntl_console_dry")) global.ntl_console_dry = 0;
if (!global.ntl_console_dry) return 0;
ntl_console_log(ntl_ts("dry.would", [_cmd, _detail]));
ntl_console_log(ntl_t("dry.nodiff"));
return 1;
