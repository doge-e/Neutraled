/// ntl_is_ascii(s) —— 全是 ASCII 返回 1（决定要不要切 CJK 字体：纯 ASCII 就用游戏自己的字体才合群）
var _s = string(argument[0]);
var _n = string_length(_s);
for (var _i = 1; _i <= _n; _i += 1)
{
    if (string_ord_at(_s, _i) > 127) return 0;
}
return 1;