/// ntl_mod_registry_init() —— mod 互操作注册表初始化
///
/// 目标（用户要求）：**mod 可以对自己不冲突的任何 mod 完全支配 / 任意联动**
///
/// 三类能力：
///   1) 跨 mod 调用      ntlm("other.mod"):func(args)      —— 直接调别人的函数
///   2) 跨 mod 事件      ntl.mod_emit("event", data)       —— 广播/监听
///   3) 跨 mod 共享状态  ntl.shared_set/get(key, value)    —— 共享数据总线
///
/// 安全性：所有跨 mod 调用都记录来源，出问题时能定位到"谁调了谁"。
global.ntl_mod_reg = ds_map_create();      // modId -> ds_map（导出表：name/author/version/exports/state）
global.ntl_mod_events = ds_map_create();   // eventName -> ds_list（订阅者）
global.ntl_shared = ds_map_create();       // 共享键值总线
ntl_log("mod", "mod 互操作注册表已初始化");
return 1;
