/// ntl_inst_destroy(inst) —— 销毁实例（mod 可以移除游戏里的任何东西）
var _inst = argument[0];
if (!instance_exists(_inst)) return 0;
instance_destroy(_inst);
return 1;
