// ntl_hook(event_name, handler_script) —— 订阅事件（handler 为脚本资源索引或函数）
// 返回订阅序号（失败返回 -1）
var _ev = string(argument[0]);
var _fn = argument[1];
if (!variable_global_exists("ntl_hooks")) global.ntl_hooks = ds_map_create();
// ★ 缺陷 1 配套：文档（docs/CHAPTER_DEV.md:147）提醒"裸脚本名会静默返回 -1"，这里补留痕
if (is_undefined(_fn)) { ntl_log("hook", "[订阅失败] " + _ev + " 的 handler 是 undefined（脚本名没解析到？先用 asset_get_index(\"...\") 再挂）"); return -1; }

var _list = ds_map_find_value(global.ntl_hooks, _ev);
if (is_undefined(_list))
{
    _list = ds_list_create();
    ds_map_add(global.ntl_hooks, _ev, _list);
}
ds_list_add(_list, _fn);
// ★ 缺陷 1 配套：订阅动作留痕（只在订阅时发生，不是每帧）。
//   这样"ntl_hook 返回 0 却没人跑"在 dr-api.log 里能一眼看出来。
ntl_log("hook", "[订阅] " + _ev + "（第 " + string(ds_list_size(_list)) + " 个订阅者）");
return ds_list_size(_list) - 1;
