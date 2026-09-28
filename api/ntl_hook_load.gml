/// ntl_hook_load(handler, modDir, source) —— 加载 hook 脚本（按需缓存源码）
///  返回可用于执行的源码字符串；已缓存则直接返回
var _handler = string(argument[0]);
var _modDir = string(argument[1]);
var _source = (argument_count > 2) ? string(argument[2]) : "lua";

if (!variable_global_exists("ntl_hook_src")) global.ntl_hook_src = ds_map_create();
var _key = _modDir + "|" + _handler;
if (ds_map_exists(global.ntl_hook_src, _key)) return ds_map_find_value(global.ntl_hook_src, _key);

var _path = _modDir + _handler;
if (!file_exists(_path))
{
    ntl_log("hook", "[错误] hook 脚本缺失: " + _path);
    ds_map_add(global.ntl_hook_src, _key, "");
    return "";
}
var _src = ntl_live_file_read(_path);
ds_map_add(global.ntl_hook_src, _key, _src);
return _src;
