/// ntl_api_version_check(modApiVersion) —— 检查 mod 声明的 API 版本是否兼容
/// mod.json 可声明 "api_version": "1.0" —— 若主版本号不匹配则警告
var _want = string(argument[0]);
var _cur = global.ntl_version;      // 例如 "1.0.0"
if (_want == "") return 1;

// 没有小数点的版本号（例如 "1"）也当成主版本号 —— 否则 string_pos 返回 0，string_copy 取到空串会误报
var _wp = string_pos(".", _want);
var _cp = string_pos(".", _cur);
var _wMaj = (_wp > 1) ? string_copy(_want, 1, _wp - 1) : _want;
var _cMaj = (_cp > 1) ? string_copy(_cur, 1, _cp - 1) : _cur;
if (_wMaj == _cMaj) return 1;

ntl_log("api", "[警告] mod 声明的 API 版本 " + _want + " 与当前 " + _cur + " 主版本不同，可能不兼容");
return 0;
