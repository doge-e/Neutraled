/// ntl_obj_init() —— 初始化 Kristal 对象实例管理器
if (!variable_global_exists("ntl_objects")) global.ntl_objects = ds_list_create();
if (!variable_global_exists("ntl_obj_count")) global.ntl_obj_count = 0;
return 1;
