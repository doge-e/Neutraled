/// ntl_lua_eval_args(env, argNodes) —— 求值参数列表（最后一个表达式若为多值则展开）
var _env = argument[0];
var _nodes = argument[1];
var _out = [];
var _cnt = array_length(_nodes);
for (var _i = 0; _i < _cnt; _i += 1)
{
    var _v = ntl_lua_ex(_env, _nodes[_i]);
    if (_i == _cnt - 1 && is_array(_v)) { for (var _j = 0; _j < array_length(_v); _j += 1) array_push(_out, _v[_j]); }
    else array_push(_out, _v);
}
return _out;
