/// ntl_settings_row_press() —— 设置菜单里接管第 6/7/8 行（coord 5/6/7）的确认键
/// 由 builder 注入 obj_darkcontroller 的 Step（button1 分支内、原版 == 0 判断之前）：
///   if (ntl_settings_row_press()) { } if (global.submenucoord[30] == 0)
/// ★ 为什么要有这个函数：入口行插在原版第 6 行（coord 5）——和 mods/deltarune_60_fps 的
///   「Mod Settings」一样，原版「Return to Title」「Back」被顺移到 coord 6 / 7。
///   与其在三处改原版比较值，不如把 5/6/7 三行的分派收在这里一处维护
///   （原版那两个判断已被改成 105/106，永不命中，见 builder/Injector.cs）。
/// coord 5 = 打开 Mod 设置面板；coord 6 = 原版 Return to Title；coord 7 = 原版 Back。
/// 返回值只是「已接管」的记号；调用方后面照常走原版分支（原版分支里没有 5/6/7，不会再动手）。
if (!variable_global_exists("submenu")) return 0;
if (!variable_global_exists("submenucoord")) return 0;
if (global.submenu != 30) return 0;

var _c = global.submenucoord[30];
if (_c == 5)
{
    ntl_modmenu_open();
    return 1;
}
if (_c == 6)
{
    global.submenu = 34;    // 原版：Return to Title（确认后回标题）
    return 1;
}
if (_c == 7)
{
    m_quit = 1;             // 原版：Back（关掉设置菜单，回游戏）
    cancelnoise = 1;
    return 1;
}
return 0;
