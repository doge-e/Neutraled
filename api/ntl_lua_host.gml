/// ntl_lua_host(name, args) —— Lua 标准库的宿主实现（由 ntl_call_host 分派 __lua_* 调用）
var _name = string(argument[0]);
var _args = argument[1];
if (_args == undefined) _args = [];
var _na = array_length(_args);
// ---- load / loadstring ----
if (_name == "__lua_load" || _name == "__lua_loadstring")
{
    var _cs = (_na > 0) ? string(_args[0]) : "";
    var _cn = (_na > 1) ? string(_args[1]) : "=(load)";
    return ntl_lua_load(_cs, _cn);
}
// ---- io 库 ----
if (string_copy(_name, 1, 5) == "__io_") return ntl_lua_io(string_delete(_name, 1, 5), (_na > 0) ? _args[0] : undefined, (_na > 1) ? _args[1] : undefined);
// ---- debug ----
if (string_copy(_name, 1, 6) == "__dbg_") return ntl_lua_debug(string_delete(_name, 1, 6), (_na > 0) ? _args[0] : undefined, (_na > 1) ? _args[1] : undefined);
// ---- love.event ----
if (string_copy(_name, 1, 10) == "__loveev_") return ntl_love_event(string_delete(_name, 1, 10), (_na > 0) ? _args[0] : undefined, (_na > 1) ? _args[1] : undefined);
if (_args == undefined) _args = [];
// LOVE2D API 分派
if (string_copy(_name, 1, 7) == "__love_") return ntl_love_host(_name, _args);
// Kristal 兼容层分派
if (string_copy(_name, 1, 5) == "__kr_") return ntl_kristal_host(_name, _args);
var _argc = array_length(_args);
var _a0 = (_argc > 0) ? _args[0] : undefined;
var _a1 = (_argc > 1) ? _args[1] : undefined;
var _a2 = (_argc > 2) ? _args[2] : undefined;

switch (_name)
{
    // ---- 基础 ----
    case "__lua_print":
    {
        var _s = "";
        for (var _i = 0; _i < array_length(_args); _i += 1)
        {
            if (_i > 0) _s += chr(9);
            _s += ntl_lua_tostring(_args[_i]);
        }
        ntl_log("lua", _s);
        return undefined;
    }
    case "__lua_tostring": return ntl_lua_tostring(_a0);
    case "__lua_tonumber": return ntl_lua_tonumber(_a0);
    case "__lua_type":
    {
        if (_a0 == undefined) return "nil";
        if (is_real(_a0))
        {
            if (ntl_lua_is_fn(_a0)) return "function";
            if (ntl_lua_is_table(_a0)) return "table";
            return "number";
        }
        if (is_string(_a0)) return "string";
        if (is_bool(_a0)) return "boolean";
        return "table";
    }
    case "__lua_pairs":  return _a0;
    case "__lua_ipairs": return _a0;
    case "__lua_rawget": return ntl_lua_table_get(_a0, _a1);
    case "__lua_rawset": return ntl_lua_table_set(_a0, _a1, _a2);
    case "__lua_error":  ntl_lua_rt_err(string(_a0)); return undefined;
    case "__lua_assert":
    {
        if (!ntl_lua_truthy(_a0)) { ntl_lua_rt_err((_a1 != undefined) ? string(_a1) : "assertion failed!"); return undefined; }
        return _a0;
    }
    case "__lua_pcall":
    {
        var _oldErr = global.ntl_lua_err;
        global.ntl_lua_err = "";
        var _r = ntl_lua_call(_a0, (_a1 != undefined && is_array(_a1)) ? _a1 : []);
        var _out = [];
        if (global.ntl_lua_err != "") { array_push(_out, false); array_push(_out, global.ntl_lua_err); global.ntl_lua_err = _oldErr; }
        else { array_push(_out, true); array_push(_out, _r); }
        return _out;
    }
    case "__lua_unpack":
    {
        var _o = [];
        for (var _j = 1; _j <= ntl_lua_table_len(_a0); _j += 1) array_push(_o, ntl_lua_table_get(_a0, _j));
        return _o;
    }
    case "__lua_select":
    {
        if (_a0 == "#") return array_length(_args) - 1;
        var _from = round(_a0);
        var _o2 = [];
        for (var _k = _from; _k < array_length(_args); _k += 1) array_push(_o2, _args[_k]);
        return _o2;
    }
    case "__lua_require": return ntl_lua_require(string(_a0));
    case "__lua_setmetatable": return ntl_lua_setmetatable(_a0, _a1);
    case "__lua_getmetatable": return ntl_lua_getmetatable(_a0);
    case "__lua_rawequal": return (_a0 == _a1) ? true : false;
    case "__lua_noop": return undefined;
    case "__lua_os_time": return date_current_datetime() * 86400;
    // os.clock：高精度秒（current_time 是毫秒）
    case "__lua_os_clock": return current_time / 1000;
    // os.date：返回可读时间字符串
    case "__lua_os_date": return string(current_time) + " " + string(current_day);
    case "__lua_next":
    {
        // next(t, k)：返回下一个键值对（简化为遍历整个表）
        var _keys = ntl_dsmap_keys(_a0);
        var _pos = -1;
        for (var _ni = 0; _ni < array_length(_keys); _ni += 1)
        {
            if (string_copy(string(_keys[_ni]), 1, 1) == chr(1)) continue;
            if (_a1 == undefined || _keys[_ni] == ntl_lua_key(_a1))
            { _pos = _ni; break; }
        }
        for (var _nj = _pos + 1; _nj < array_length(_keys); _nj += 1)
        {
            if (string_copy(string(_keys[_nj]), 1, 1) == chr(1)) continue;
            var _o = [];
            array_push(_o, ntl_lua_key_decode(_keys[_nj]));
            array_push(_o, ds_map_find_value(_a0, _keys[_nj]));
            return _o;
        }
        return undefined;
    }
    case "__lua_gml_call":
    {
        // ntl.call("脚本名", ...) → 直接调用 GML 脚本
        var _fn = string(_a0);
        var _rest = [];
        for (var _m = 1; _m < array_length(_args); _m += 1) array_push(_rest, _args[_m]);
        return ntl_call_host(_fn, _rest);
    }

    // ---- math ----
    case "__lua_math_floor": return floor(real(_a0));
    case "__lua_math_ceil":  return ceil(real(_a0));
    case "__lua_math_abs":   return abs(real(_a0));
    case "__lua_math_sqrt":  return sqrt(real(_a0));
    case "__lua_math_sin":   return sin(real(_a0));
    case "__lua_math_cos":   return cos(real(_a0));
    case "__lua_math_random":
    {
        if (array_length(_args) == 0) return random(1);
        if (array_length(_args) == 1) return irandom(round(_a0) - 1) + 1;
        return irandom_range(round(_a0), round(_a1));
    }
    case "__lua_math_min": return min(real(_a0), real(_a1));
    case "__lua_math_max": return max(real(_a0), real(_a1));
    case "__lua_math_fmod": return real(_a0) mod real(_a1);
    case "__lua_math_pow": return power(real(_a0), real(_a1));

    // ---- string ----
    case "__lua_str_len":   return string_length(ntl_lua_tostring(_a0));
    case "__lua_str_sub":
    {
        var _s2 = ntl_lua_tostring(_a0);
        var _len = string_length(_s2);
        var _i2 = round(_a1);
        var _j2 = (_a2 != undefined) ? round(_a2) : _len;
        if (_i2 < 0) _i2 = _len + _i2 + 1;
        if (_j2 < 0) _j2 = _len + _j2 + 1;
        if (_i2 < 1) _i2 = 1;
        if (_j2 > _len) _j2 = _len;
        if (_j2 < _i2) return "";
        return string_copy(_s2, _i2, _j2 - _i2 + 1);
    }
    case "__lua_str_upper": return string_upper(ntl_lua_tostring(_a0));
    case "__lua_str_lower": return string_lower(ntl_lua_tostring(_a0));
    case "__lua_str_find":
    {
        var _p = string_pos(ntl_lua_tostring(_a1), ntl_lua_tostring(_a0));
        return (_p > 0) ? _p : undefined;
    }
    case "__lua_str_rep":
    {
        var _s3 = ntl_lua_tostring(_a0);
        var _n = round(_a1);
        var _acc = "";
        for (var _q = 0; _q < _n; _q += 1) _acc += _s3;
        return _acc;
    }
    case "__lua_str_format": return ntl_lua_tostring(_a0);   // 简化：不做完整格式化
    case "__lua_str_char":  return chr(round(_a0));
    case "__lua_str_byte":  return ord(ntl_lua_tostring(_a0));

    // ---- table ----
    case "__lua_tab_insert":
    {
        var _tab = _a0;
        var _l = ntl_lua_table_len(_tab);
        if (array_length(_args) >= 3) ntl_lua_table_set(_tab, round(_a1), _a2);
        else ntl_lua_table_set(_tab, _l + 1, _a1);
        return undefined;
    }
    case "__lua_tab_remove":
    {
        var _tab2 = _a0;
        var _l2 = ntl_lua_table_len(_tab2);
        var _pos = (_a1 != undefined) ? round(_a1) : _l2;
        var _old = ntl_lua_table_get(_tab2, _pos);
        for (var _r2 = _pos; _r2 < _l2; _r2 += 1)
            ntl_lua_table_set(_tab2, _r2, ntl_lua_table_get(_tab2, _r2 + 1));
        ntl_lua_table_set(_tab2, _l2, undefined);
        return _old;
    }
    case "__lua_tab_concat":
    {
        var _tab3 = _a0;
        var _sep = (_a1 != undefined) ? ntl_lua_tostring(_a1) : "";
        var _l3 = ntl_lua_table_len(_tab3);
        var _acc2 = "";
        for (var _t = 1; _t <= _l3; _t += 1)
        {
            if (_t > 1) _acc2 += _sep;
            _acc2 += ntl_lua_tostring(ntl_lua_table_get(_tab3, _t));
        }
        return _acc2;
    }
}
// ★ 兜底：不是标准库函数名 → 尝试当**真实 GML 脚本**调用
//   这条路径是 mod 里 ntl.screenshot() / ntl.goto_chapter() 走的，
//   之前没有兜底会静默返回 undefined（doctor 抓到的真实缺口）。
var _si = asset_get_index("gml_Script_" + _name);
if (_si != -1)
{
    if (_na == 0) return script_execute(_si);
    if (_na == 1) return script_execute(_si, _args[0]);
    if (_na == 2) return script_execute(_si, _args[0], _args[1]);
    if (_na == 3) return script_execute(_si, _args[0], _args[1], _args[2]);
    if (_na == 4) return script_execute(_si, _args[0], _args[1], _args[2], _args[3]);
    if (_na == 5) return script_execute(_si, _args[0], _args[1], _args[2], _args[3], _args[4]);
    if (_na == 6) return script_execute(_si, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5]);
}

return undefined;
