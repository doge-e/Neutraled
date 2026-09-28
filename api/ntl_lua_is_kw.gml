/// ntl_lua_is_kw(name) —— Lua 关键字判断
var _n = string(argument[0]);
switch (_n)
{
    case "and": case "break": case "do": case "else": case "elseif": case "end":
    case "false": case "for": case "function": case "goto": case "if": case "in":
    case "local": case "nil": case "not": case "or": case "repeat": case "return":
    case "then": case "true": case "until": case "while":
        return 1;
}
return 0;
