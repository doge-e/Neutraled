/// ntl_lua_tok_push(list, type, value, line) —— 追加一个 token
var _list = argument[0];
var _type = argument[1];
var _value = argument[2];
var _line = argument[3];

var _t = ds_map_create();
ds_map_add(_t, "t", _type);      // kw / name / num / str / op / eof
ds_map_add(_t, "v", _value);
ds_map_add(_t, "line", _line);
ds_list_add(_list, _t);
return _t;

