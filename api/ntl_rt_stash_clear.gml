/// ntl_rt_stash_clear() —— 丢掉挂起的启动请求（只在上限时间内始终修不好时调用）
global.ntl_rt_pending_txt = "";
global.ntl_rt_pending_name = "";
global.ntl_rt_retry_frame = 0;
return 1;