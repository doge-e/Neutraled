/// ntl_modmenu_find_action(act) —— 当前视图里第一个 action == act 的行号（找不到返回 0）
/// 用途：从语言列表返回主列表时要停在「界面语言」那一行，但不能写死下标 ——
/// 主列表现在会插 mod 分组标题，下标会漂。
var _a = string(argument[0]);
var _rows = ntl_modmenu_rows();
for (var _i = 0; _i < array_length(_rows); _i += 1)
{
    var _r = _rows[_i];
    if (array_length(_r) > 5 && string(_r[5]) == _a) return _i;
}
return 0;
