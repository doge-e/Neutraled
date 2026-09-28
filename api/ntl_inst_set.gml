/// ntl_inst_set(inst, varName, value) —— 设置任意实例的任意变量（完全支配）
var _inst = argument[0];
var _name = string(argument[1]);
var _v = (argument_count > 2) ? argument[2] : undefined;
if (!instance_exists(_inst)) return 0;
// ★ F5 接线 ntl_sprite_resolve：写 sprite_index 时把 ntl_sprite_replace 登记过的替换解析出来，
//   否则 mod 换皮之后 sprite_index 仍指向原精灵，替换永远不显示。
if (_name == "sprite_index" && is_real(_v)) _v = ntl_sprite_resolve(_v);
try { variable_instance_set(_inst, _name, _v); return 1; } catch (e) { return 0; }
