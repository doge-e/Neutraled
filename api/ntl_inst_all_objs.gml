/// ntl_inst_all_objs() —— 列出当前房间的所有对象名（mod 可以遍历并操作任意对象）
var _seen = ds_map_create();
var _out = [];
var _n = instance_count;
for (var _i = 0; _i < _n; _i += 1)
{
    var _inst = instance_id_get(_i);
    if (!instance_exists(_inst)) continue;
    var _o = _inst.object_index;
    var _nm = object_get_name(_o);
    if (!ds_map_exists(_seen, _nm))
    {
        ds_map_add(_seen, _nm, 1);
        array_push(_out, _nm);
    }
}
ds_map_destroy(_seen);
return _out;
