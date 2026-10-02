/// ntl_autoskip() —— 顶层：按配置自动跳过章节选择器，直接进入目标章节

// ⚠ 以前这里要求官方 obj_CHAPTER_SELECT 实例存在 —— 但接管时我们把它（连同整条层级）**销毁**了，
//   于是自动跳过永远提前 return，配置形同虚设（实测：auto_skip_selector=1 完全没反应）。
if (ntl_is_root() != 1) return 0;
if (global.ntl_ch_loaded != 1) return 0;
if (!variable_global_exists("ntl_cfg")) return 0;

if (global.ntl_autoskip_done == 1) return 0;
if (ds_map_find_value(global.ntl_cfg, "auto_skip_selector") != 1) return 0;

var _delay = ds_map_find_value(global.ntl_cfg, "auto_skip_delay");
if (global.ntl_autoskip_frame < _delay)
{
    global.ntl_autoskip_frame += 1;
    return 0;
}
global.ntl_autoskip_done = 1;

// 优先用 id 指定；否则用章节序号
var _id = ds_map_find_value(global.ntl_cfg, "auto_chapter_id");
if (string_length(_id) > 0 && global.ntl_ch_loaded == 1)
{
    var _n = array_length(global.ntl_ch);
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _m = global.ntl_ch[_i];
        if (ds_map_find_value(_m, "id") == _id)
        {
            var _kind = ds_map_find_value(_m, "kind");
            var _order = real(ds_map_find_value(_m, "order"));
            if (_kind == "external")
            {
                ntl_log("auto", "自动启动外部章节: " + _id);
                return ntl_ext_launch(_m);
            }
            if (_kind == "timeline")
            {
                var _dir = ds_map_find_value(_m, "dir");
                var _params2 = "";
                try { _params2 = get_chapter_switch_parameters(); } catch (e) { _params2 = " launcher"; }
                ntl_log("auto", "自动进入时间线章节: " + _id);
                // ★ 看门狗现场（2026-09-30）：见 api/ntl_chg_watch.gml
                global.ntl_chg_pending = 1;
                global.ntl_chg_frame = global.ntl_frames;
                global.ntl_chg_try = 0;
                global.ntl_chg_dir = _dir;
                global.ntl_chg_full = "/" + _dir;
                global.ntl_chg_kind = "timeline";
                global.ntl_chg_order = _order;
                global.ntl_chg_args = "-game data.win" + _params2;
                global.ntl_chg_wd = working_directory;
                global.ntl_chg_pd = program_directory;
                // ★ t35 F3（t30 真机 §12：契约要求的 ntl_chg*/working_directory/program_directory/
                //   parameter_string/ntl_is_root 在正常路径不落盘）——登记现场时一次性写全，真机 review 不必再猜。
                var _chgps = "";
                // ★ 2026-10-02 真机实证：parameter_string() 在本运行时直接 0xc0000005 崩掉整个进程
      //   （Windows 事件日志 Application Error id=1000 四次同桶），表现为「按 Z 进章节后进程消失」。
      //   已彻底移除该调用，不要再加回来。
      _chgps = "<parameter_string 不可用>";
                var _chgmsg = "[chg] dir=" + _dir + " pfx=" + "/" + " full=" + "/" + _dir
                        + " kind=timeline order=" + string(_order) + " wd=" + working_directory + " pd=" + program_directory
                        + " parameter_string=" + _chgps + " args=" + ("-game data.win" + _params2) + " ntl_is_root=" + string(ntl_is_root())
                        + " frame=" + string(global.ntl_frames);
                ntl_log("auto", _chgmsg);
                game_change("/" + _dir, "-game data.win" + _params2);
                return 1;
            }
            ntl_log("auto", "自动进入章节 " + string(_order) + "（id=" + _id + "）");
            return ntl_goto_chapter(_order);
        }
    }
    ntl_log("auto", "[警告] 未找到章节 id: " + _id);
    return 0;
}

var _ch = ds_map_find_value(global.ntl_cfg, "auto_chapter");
if (_ch >= 1)
{
    ntl_log("auto", "自动进入章节 " + string(_ch));
    return ntl_goto_chapter(_ch);
}
return 0;
