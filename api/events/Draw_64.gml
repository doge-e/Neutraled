// obj_ntl_core :: Draw GUI —— 最上层绘制
// 顺序：先游戏界面，再 Neutraled 地图/玩家，最后控制台（永远最上）
ntl_root_draw();                                    // 章节选择器界面

// Neutraled 地图导航演示（有玩家时接管画面）
if (variable_global_exists("ntl_player") && variable_global_exists("ntl_test_map"))
{
    var _mp = global.ntl_test_map;
    var _cx = variable_global_exists("ntl_cam_x") ? global.ntl_cam_x : 0;
    var _cy = variable_global_exists("ntl_cam_y") ? global.ntl_cam_y : 0;
    if (ds_map_find_value(_mp, "ok") == 1)
    {
        // 全屏底色（避免透出下层）
        draw_set_alpha(1);
        draw_set_color(c_black);
        draw_rectangle(0, 0, room_width, room_height, false);
        // 地图
        ntl_map_draw(_mp, _cx, _cy);
        // Kristal 对象实例
        ntl_obj_draw(_cx, _cy);
        // 玩家
        ntl_player_draw();
        // 附近的交互对象：画高亮框 + 提示
        if (variable_global_exists("ntl_near_obj") && global.ntl_near_obj != undefined)
        {
            var _no = global.ntl_near_obj;
            var _ox = ds_map_find_value(_no, "x") - _cx;
            var _oy = ds_map_find_value(_no, "y") - _cy;
            var _ow = ds_map_find_value(_no, "w");
            var _oh = ds_map_find_value(_no, "h");
            draw_set_alpha(0.6);
            draw_set_color(c_yellow);
            if (_ow > 0 && _oh > 0) draw_rectangle(_ox, _oy, _ox + _ow, _oy + _oh, true);
            else draw_circle(_ox, _oy, 10, true);
            draw_set_alpha(1);
            draw_set_color(c_yellow);
            draw_text(_ox, _oy - 18, "[E] " + string(global.ntl_near_name));
        }
        else
        {
            // 附近的交互对象：画高亮框 + 提示
        }

        // 提示条
        draw_set_color(c_white);
        draw_text(12, 8, "Neutraled  |  " + ntl_t("hud.move") + "  |  " + ntl_t("hud.interact") + "  |  " + ntl_t("hud.console"));
        var _ic = variable_global_exists("ntl_interact_count") ? global.ntl_interact_count : 0;
        var _nc = variable_global_exists("ntl_near_count") ? global.ntl_near_count : 0;
        draw_text(12, 26, ntl_t("hud.near") + ": " + string(_nc) + "   " + ntl_t("hud.interacted") + ": " + string(_ic));
        if (variable_global_exists("ntl_interact_name") && string_length(string(global.ntl_interact_name)) > 0)
            draw_text(12, 44, ntl_t("hud.last_interact") + ": " + string(global.ntl_interact_name));
    }
}

ntl_draw_mods();
try { ntl_loop_emit("draw"); } catch (e) { ntl_log("loop", "[ntl] events/Draw_64.gml:56 ntl_loop_emit(draw) 捕获: " + string(e)); }
ntl_live_emit("on_draw", 0);
// Mod 设置面板不在 GUI 层画：它是游戏 submenu 51 的一页，由 obj_darkcontroller 的注入代码绘制
if (global.ntl_console_open) ntl_console_draw();   // 控制台永远最上
return 0;