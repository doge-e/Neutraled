/// ntl_lua_is_stdlib(name) —— 是否 Lua 标准库函数名
var _n = string(argument[0]);
switch (_n)
{
    case "print": case "tostring": case "tonumber": case "type":
    case "pairs": case "ipairs": case "rawget": case "rawset":
    case "error": case "assert": case "pcall": case "unpack":
    case "select": case "require":
        return 1;
}
if (string_copy(_n, 1, 4) == "str_") return 1;
if (string_copy(_n, 1, 5) == "tab_") return 1;
if (string_copy(_n, 1, 5) == "math_") return 1;
return 0;
