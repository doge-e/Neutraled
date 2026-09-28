/// ntl_obj_count_now(objName) —— 统计某对象名当前"活跃"的实例数
/// 与 ntl_obj_count 的区别：跳过已标记销毁的实例
var _name = string(argument[0]);
if (_name == "") return 0;
var _idx = asset_get_index(_name);
if (_idx == -1) return 0;

var _cnt = 0;
var _n = instance_count;
for (var _i = 0; _i < _n; _i += 1)
{
    var _inst = instance_id_get(_i);
    if (!instance_exists(_inst)) continue;
    if (_inst.object_index == _idx) _cnt += 1;
}
return _cnt;
