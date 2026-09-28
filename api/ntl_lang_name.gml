/// ntl_lang_name(code) —— 语言的母语名字（面板语言列表用；认不出来就回大写代码）
var _c = string_lower(string(argument[0]));
switch (_c)
{
    case "zh": return "中文";
    case "en": return "English";
    case "ja": return "日本語";
    case "de": return "Deutsch";
    case "es": return "Español";
    case "fr": return "Français";
    case "pt": return "Português";
    case "ru": return "Русский";
    case "ko": return "한국어";
    case "it": return "Italiano";
}
return string_upper(_c);