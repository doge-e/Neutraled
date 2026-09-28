/// ntl_inst_create(objName, x, y, depth) —— 运行时创建任意对象的实例
/// mod 用它可以在游戏里"凭空生成"东西（NPC、道具、特效…）
var _name = string(argument[0]);
var _x = (argument_count > 1) ? real(argument[1]) : 0;
var _y = (argument_count > 2) ? real(argument[2]) : 0;
var _d = (argument_count > 3) ? real(argument[3]) : 0;

var _obj = asset_get_index(_name);
if (_obj < 0 || !object_exists(_obj))
{
    ntl_log("inst", "[错误] 对象不存在: " + _name);
    return noone;
}
var _inst = instance_create_depth(_x, _y, _d, _obj);
ntl_log("inst", "已创建实例 " + _name + " @(" + string(_x) + "," + string(_y) + ") id=" + string(_inst));
return _inst;
