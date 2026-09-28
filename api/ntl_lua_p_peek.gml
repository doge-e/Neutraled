/// ntl_lua_p_peek(s, offset=0) —— 看第 offset 个 token（不消费）
var _s = argument[0];
var _off = (argument_count > 1) ? argument[1] : 0;
if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return undefined;
var _toks = ds_map_find_value(_s, "toks");
var _i = ds_map_find_value(_s, "pos") + _off;
if (_i < 0) _i = 0;
if (_i >= ds_list_size(_toks)) _i = ds_list_size(_toks) - 1;
return ds_list_find_value(_toks, _i);
