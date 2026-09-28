/// ntl_console_key_vk(keyName) —— 键名 → 虚拟键码
var _k = string_upper(string(argument[0]));
switch (_k)
{
    case "F1": return 112; case "F2": return 113; case "F3": return 114;
    case "F4": return 115; case "F5": return 116; case "F6": return 117;
    case "F7": return 118; case "F8": return 119; case "F9": return 120;
    case "F10": return 121; case "F11": return 122; case "F12": return 123;
    case "SPACE": return 32; case "ENTER": return 13; case "ESC": return 27;
    case "TAB": return 9; case "BACKSPACE": return 8;
    case "UP": return 38; case "DOWN": return 40; case "LEFT": return 37; case "RIGHT": return 39;
}
if (string_length(_k) == 1) return ord(_k);
return 0;
