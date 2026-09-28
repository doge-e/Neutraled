/// ntl_modmenu_page_step() —— Mod 设置页（submenu 51）的输入：照游戏自己的二级菜单写
/// 由 builder 注入 obj_darkcontroller 的 Step（menuno==5 块里、与 submenu==34 同级，每帧跑）。
/// 键位与游戏一致：↑↓ 移光标（movenoise + 循环）、Z/Enter 确认（button1_p + onebuffer<0）、
/// X 返回（button2_p + twobuffer<0，回设置菜单）；语言行额外支持 ←→ 快切
/// （官方 submenu 36 的边框选择也是这个做法）。
/// ★ onebuffer/twobuffer 是游戏自己的按键缓冲：这里必须沿用，否则同一次按键会在
///   打开面板的那一帧被面板自己再吃一次。
/// ★ 本轮（用户 m17504）改动：
///   - 光标移动走 ntl_modmenu_move()：跳过分组标题（纯排版行不可选），不再靠写死的行号；
///   - 确认动作按**行自带的 action** 分派（"chapters"/"mods"/"lang"/"deploy"/"close"/"script:<名字>"），
///     不再写死 _coord == 0/1/2/3 —— 主列表里插了分组标题之后，下标会漂；
///   - mod 注册项按**脚本名**调用（ntl_menu_run_script），不再用下标算（分组标题会让下标错位）；
///   - 每次按键后清空行缓存（开关值/计数会变，不能让缓存骗人）。
if (!variable_global_exists("submenu") || global.submenu != 51) return 0;
if (!variable_global_exists("submenucoord")) return 0;

var _view = variable_global_exists("ntl_modmenu_view") ? string(global.ntl_modmenu_view) : "main";
var _slot = ntl_modmenu_slot(_view);
if (array_length(global.submenucoord) <= _slot) global.submenucoord[_slot] = 0;   // 原版只初始化了 0..35（Create 里 for i<36），我们自己补上 51+
var _n = ntl_modmenu_count();
if (_n <= 0) return 0;

var _coord = real(global.submenucoord[_slot]);
if (_coord < 0) _coord = 0;
if (_coord >= _n) _coord = _n - 1;
// 切视图/老存档可能停在分组标题上：挪到下一个可选行（否则按 Z 会掉进一个没有动作的行）
if (!ntl_modmenu_selectable(_coord)) _coord = ntl_modmenu_move(_coord, 1);

if (up_p())
{
    movenoise = 1;
    _coord = ntl_modmenu_move(_coord, -1);      // 循环 + 跳过分组标题
}
if (down_p())
{
    movenoise = 1;
    _coord = ntl_modmenu_move(_coord, 1);
}
global.submenucoord[_slot] = _coord;

var _row = ntl_modmenu_row(_coord);
var _act = (array_length(_row) > 5) ? string(_row[5]) : "";

// 「界面语言」行：←→ 直接快切，不用进列表
if (_view == "main" && _act == "lang")
{
    if (left_p())  { ntl_modmenu_lang_cycle(-1); global.ntl_modmenu_rows_cache = undefined; return 1; }
    if (right_p()) { ntl_modmenu_lang_cycle(1);  global.ntl_modmenu_rows_cache = undefined; return 1; }
}

if (button2_p() && twobuffer < 0)
{
    twobuffer = 2;
    if (_view == "main")
    {
        ntl_modmenu_close();       // 回设置菜单（submenu 30）
    }
    else
    {
        global.ntl_modmenu_view = "main";
        global.ntl_modmenu_rows_cache = undefined;
        cancelnoise = 1;
    }
    return 1;
}

if (button1_p() && onebuffer < 0)
{
    onebuffer = 2;
    twobuffer = 2;
    selectnoise = 1;
    global.ntl_modmenu_rows_cache = undefined;      // 开关值/计数会变，行列表重算

    if (_view == "main")
    {
        if (_act == "chapters")
        {
            global.ntl_modmenu_view = "chapters";
            global.submenucoord[52] = 0;
        }
        else if (_act == "mods")
        {
            global.ntl_modmenu_mods = ntl_modmenu_mods();
            global.ntl_modmenu_view = "mods";
            global.submenucoord[54] = 0;
        }
        else if (_act == "lang")
        {
            // 进语言列表：光标停到当前语言上（看得见「现在用哪个」）
            global.ntl_modmenu_view = "langs";
            var _list = ntl_lang_list();
            var _cur = string_lower(string(global.ntl_lang));
            var _idx = 0;
            for (var _i = 0; _i < array_length(_list); _i += 1)
            {
                if (string_lower(string(_list[_i])) == _cur) _idx = _i;
            }
            global.submenucoord[53] = _idx;
        }
        else if (_act == "deploy")
        {
            ntl_modmenu_deploy();
        }
        else if (_act == "close")
        {
            ntl_modmenu_close();
        }
        else if (string_pos("script:", _act) == 1)
        {
            // mod 注册的面板项：跑它的动作脚本（mod 开发者接口 ntl_menu_add）
            if (ntl_menu_run_script(string_delete(_act, 1, 7)) == 0)
            {
                global.ntl_modmenu_msg = ntl_t("menu.mods_ro");
                global.ntl_modmenu_msg_frames = 90;
            }
        }
    }
    else if (_view == "langs")
    {
        var _codes = ntl_lang_list();
        if (_coord >= 0 && _coord < array_length(_codes)) ntl_modmenu_lang_set(_codes[_coord]);
        global.ntl_modmenu_view = "main";
        global.ntl_modmenu_rows_cache = undefined;
        global.submenucoord[51] = ntl_modmenu_find_action("lang");      // 回主列表停在「界面语言」行（不写死下标）
    }
    else if (_view == "chapters")
    {
        ntl_modmenu_goto(_coord);
    }
    else
    {
        global.ntl_modmenu_msg = ntl_t("menu.mods_ro");     // mods 视图只读
        global.ntl_modmenu_msg_frames = 120;
    }
    return 1;
}
return 0;
