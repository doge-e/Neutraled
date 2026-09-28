/// ntl_obj_count(objName) —— 统计某对象名的实例数量
var _name = string(argument[0]);
if (_name == "") return 0;
var _idx = asset_get_index(_name);
if (_idx == -1) return 0;
return instance_number(_idx);
