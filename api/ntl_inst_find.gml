/// ntl_inst_find(objName) —— 按名字找对象的所有实例（返回数组）
var _name = string(argument[0]);
var _obj = asset_get_index(_name);
if (_obj < 0) return [];
var _out = [];
var _n = instance_number(_obj);
for (var _i = 0; _i < _n; _i += 1) array_push(_out, instance_find(_obj, _i));
return _out;
