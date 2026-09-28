/// ntl_mod_on(eventName, fn) —— 订阅跨 mod 事件
var _evt = string(argument[0]);
var _fn = argument[1];
if (!variable_global_exists("ntl_mod_events")) ntl_mod_registry_init();
if (!ds_map_exists(global.ntl_mod_events, _evt))
    ds_map_add(global.ntl_mod_events, _evt, ds_list_create());

var _sub = ds_map_create();
ds_map_add(_sub, "fn", _fn);
var _who = "unknown";
if (variable_global_exists("ntl_current_mod") && string(global.ntl_current_mod) != "")
    _who = string(global.ntl_current_mod);
ds_map_add(_sub, "mod", _who);
ds_list_add(ds_map_find_value(global.ntl_mod_events, _evt), _sub);
return 1;
