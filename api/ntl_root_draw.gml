/// ntl_root_draw() —— 顶层章节选择界面（覆盖官方 UI）
if (ntl_is_root() != 1) return 0;          // ★ 章节里绝对不画（否则会盖在存档界面上）
var _sel_obj = asset_get_index("obj_CHAPTER_SELECT");
if (_sel_obj < 0) return 0;
// 官方对象已被销毁（接管）→ 不依赖它
if (global.ntl_ch_loaded != 1) return 0;

var _W = display_get_gui_width();
var _H = display_get_gui_height();

// 背景（覆盖官方界面）
draw_set_alpha(1);
draw_set_color(c_black);
draw_rectangle(0, 0, _W, _H, false);

// 字体：★ 必须用 Neutraled 的中文字体
//   （游戏主字体 fnt_main 只有 96 个 ASCII 字形，中文会**整片画不出来** —— 这就是
//     "章节选择器没有中文" 的根因；之前这里注释写着"含中文"，其实并不含）
var _font = asset_get_index("ntl_font_cjk");
if (_font == -1) _font = asset_get_index("fnt_main");
if (_font == -1) _font = 8;
draw_set_font(_font);
// 关掉纹理过滤：像素字体被线性过滤后会糊掉、且非整数缩放下字距看着不匀
gpu_set_texfilter(false);
draw_set_halign(fa_left);
draw_set_valign(fa_top);

// 标题
draw_set_color(c_yellow);
draw_text(24, 14, ntl_t("root.title"));
draw_set_color(c_white);
var _pages = ntl_root_pages();
draw_text(_W - 150, 14, ntl_ts("root.page", [string(global.ntl_ch_page + 1), string(_pages)]));

// 搜索框
if (global.ntl_ch_search_mode == 1)
{
    draw_set_color(c_lime);
    draw_text(24, 40, ntl_ts("root.search", [global.ntl_ch_search]));
}
else
{
    draw_set_color(c_gray);
    draw_text(24, 40, ntl_t("root.search_hint"));
}

// 列表
var _items = ntl_root_page_items(global.ntl_ch_page);
var _y = 76;
for (var _k = 0; _k < 7; _k += 1)
{
    var _idx = _items[_k];
    var _slot = global.ntl_ch_page * 7 + _k + 1;  // 列表序号（1-based，显示用）
    var _selected = (_k == global.ntl_ch_sel);

    if (_idx < 0)
    {
        // 空章节（仿官方第 6/7 章的灰显）
        draw_set_color(c_dkgray);
        draw_text(40, _y, string(_slot) + "  --");   // 空槽（无内容）
    }
    else
    {
        var _m = global.ntl_ch[_idx];
        var _name = ds_map_find_value(_m, "name");
        var _kind = ds_map_find_value(_m, "kind");
        var _src = ds_map_find_value(_m, "source");
        var _auth = ds_map_find_value(_m, "author");
        var _en = ds_map_find_value(_m, "enabled");
        var _order = real(ds_map_find_value(_m, "order"));

        var _label = "Chapter " + string(_order);
        if (_kind == "patch") _label += ntl_t("root.tag_patch");
        else if (_kind == "timeline") _label += ntl_t("root.tag_timeline");
        else if (_kind == "external") _label += ntl_t("root.tag_external");

        var _line = _label + "   " + _name;
        if (_kind != "official" && string_length(_src) > 0)
            _line += "   —  " + _src + " (" + _auth + ")";

        if (_en != 1)
        {
            draw_set_color(c_dkgray);
            _line += ntl_t("root.no_content");
        }
        else if (_selected)
        {
            draw_set_color(c_yellow);
        }
        // 未选中时的配色：官方=白、改官方=青、时间线=品红、**外部章节=白**（用户反馈：外部章节不该是紫色，
        //   应和其它未选中项一样是白色；改由文字标签 [外] 区分，与 [改]/[线] 风格一致）
        else if (_kind == "official") draw_set_color(c_white);
        else if (_kind == "external") draw_set_color(c_white);
        else if (_kind == "patch") draw_set_color(c_aqua);
        else draw_set_color(c_fuchsia);

        if (_selected) draw_text(24, _y, "→");
        draw_text(40, _y, _line);
    }
    _y += 34;
}

// 底部提示
draw_set_color(c_gray);
draw_text(24, _H - 40, ntl_t("root.keys"));
// 告诉玩家控制台怎么开（以前界面上没有任何提示）
draw_set_color(c_lime);
draw_text(24, _H - 22, ntl_t("root.footer"));
// 语言 / 退出提示（用户要求：章节选择器要有语言设置和退出游戏按键）
draw_set_color(c_gray);
draw_text(24, _H - 58, ntl_t("root.keys2"));
// 短暂提示（语言已切换 / 语言不可用）
if (variable_global_exists("ntl_root_toast") && string_length(string(global.ntl_root_toast)) > 0 && global.ntl_root_toast_frames > 0)
{
    global.ntl_root_toast_frames -= 1;
    // ★ y 必须与退出确认（_H - 96）错开，否则两条同时出现会叠字（实测「語言已切  Esc zh-確W」）
    draw_set_color(c_yellow);
    draw_text(24, _H - 116, string(global.ntl_root_toast));
    if (global.ntl_root_toast_frames <= 0) global.ntl_root_toast = "";
}
// 退出确认 / 正在退出
if (variable_global_exists("ntl_quit_pending") && global.ntl_quit_pending > 0)
{
    draw_set_color(c_red);
    draw_text(24, _H - 96, ntl_t("root.quit.bye"));
}
else if (variable_global_exists("ntl_quit_armed") && global.ntl_quit_armed == 1)
{
    draw_set_color(c_red);
    draw_text(24, _H - 96, ntl_t("root.quit.confirm"));
}
var _cnt = array_length(global.ntl_ch_filtered);
if (string_length(global.ntl_ch_search) > 0)
{
    draw_set_color(c_gray);
    draw_text(560, _H - 22, ntl_ts("root.match", [string(_cnt)]));
}
return 1;
