/// ntl_inst_get(inst, varName) —— 读取任意实例的任意变量
var _inst = argument[0];
var _name = string(argument[1]);
if (!instance_exists(_inst)) return undefined;
try { return variable_instance_get(_inst, _name); } catch (e) { return undefined; }
