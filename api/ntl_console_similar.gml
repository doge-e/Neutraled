/// ntl_console_similar(a, b) —— 命令名相似度打分（给"你是不是想输入 xxx"用；越大越像，0 = 不像）
var _a = string_lower(string(argument[0]));
var _b = string_lower(string(argument[1]));
var _la = string_length(_a);
var _lb = string_length(_b);
if (_la <= 0 || _lb <= 0) return 0;
if (_a == _b) return 100;

var _min = min(_la, _lb);
var _pre = 0;
while (_pre < _min)
{
    if (string_char_at(_a, _pre + 1) != string_char_at(_b, _pre + 1)) break;
    _pre += 1;
}
if (_pre == _min) return 50 + _min;                                        // 一个是另一个的前缀
if (string_pos(_a, _b) > 0 || string_pos(_b, _a) > 0) return 20 + _pre;    // 包含关系
if (_pre >= 2) return _pre;                                                // 共同前缀（后半段打错）
if (string_char_at(_a, 1) == string_char_at(_b, 1)) return 2;              // 首字母相同，弱提示
return 0;
