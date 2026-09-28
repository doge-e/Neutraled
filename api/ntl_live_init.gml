/// ntl_live_init —— 初始化 live 运行时系统  [BUILD-2026-09-21-0700]
/// 1) 从 live/index.json 加载 live mod
/// 2) 从 mods/ 加载 mod 自己的 Lua 脚本（mod 互操作的前提）
/// 3) 执行 on_init
ntl_log("live", "★★★★★ LIVE-INIT-NEW-BUILD ★★★★★");
if (!variable_global_exists("ntl_live_debug")) global.ntl_live_debug = 0;

// ★ F5 接线 debug_live：config.json 的 debug_live 以前 zero 消费点 —— 现在接到 global.ntl_live_debug。
//   消费者是 ntl_live_emit.gml:44：为 1 时每个事件都会打一行
//   "  > <event> -> <file> (N chars)"（类别 live）
if (variable_global_exists("ntl_cfg") && is_real(global.ntl_cfg) && ds_exists(global.ntl_cfg, ds_type_map))
{
    if (ds_map_find_value(global.ntl_cfg, "debug_live") == 1)
    {
        global.ntl_live_debug = 1;
        ntl_log("live", "[live] 配置 debug_live=1 -> 打开 live 脚本调试埋点");
    }
}

global.ntl_live_mods = ds_list_create();
global.ntl_live_budget = 200000;
global.ntl_live_enabled = 1;

var _dir = ntl_live_live_dir();
var _idxPath = _dir + "index.json";
var _loaded = 0;

// ---------- 第 1 部分：live/ 目录 ----------
if (file_exists(_idxPath))
{
    var _txt = ntl_live_file_read(_idxPath);
    var _j = undefined;
    try { _j = json_parse(_txt); } catch (e) { _j = undefined; }
    if (_j != undefined && variable_struct_exists(_j, "mods"))
    {
        var _names = _j.mods;
        for (var _i = 0; _i < array_length(_names); _i += 1)
        {
            var _name = string(_names[_i]);
            var _mDir = _dir + _name + "/";
            var _mPath = _mDir + "mod.json";
            if (!file_exists(_mPath)) continue;
            var _mj = undefined;
            try { _mj = json_parse(ntl_live_file_read(_mPath)); } catch (e) { continue; }
            if (_mj == undefined) continue;

            // ★ F5 接线 ntl_api_version_check：mod.json 的 "api_version" 主版本不匹配就警告
            if (variable_struct_exists(_mj, "api_version"))
                ntl_api_version_check(string(variable_struct_get(_mj, "api_version")));

            var _entry = ds_map_create();
            ds_map_add(_entry, "name", _name);
            ds_map_add(_entry, "dir", _mDir);
            ds_map_add(_entry, "env", ntl_e_new_env());
            ds_map_add(_entry, "hooks", ds_map_create());
            ds_map_add(_entry, "cache", ds_map_create());
            ds_map_add(_entry, "astcache", ds_map_create());

            if (variable_struct_exists(_mj, "scripts"))
            {
                var _scripts = _mj.scripts;
                var _keys = variable_struct_get_names(_scripts);
                for (var _k = 0; _k < array_length(_keys); _k += 1)
                    ds_map_add(ds_map_find_value(_entry, "hooks"), _keys[_k], string(variable_struct_get(_scripts, _keys[_k])));
            }
            ds_list_add(global.ntl_live_mods, _entry);
            _loaded += 1;
        }
    }
}
else ntl_log("live", "live/index.json 不存在（跳过 live mod）");

// ---------- 第 2 部分：mods/ 目录（mod 互操作的前提）----------
// ⚠ 这里原来是**第二份 mods/ 加载器**，章节号取的是 config.auto_chapter（而不是当前进程的 working_directory），
//   于是 root 进程也会去加载 chapter1 作用域的 mod 脚本，而它的 live 桥 ch_<slug>_hooks() 在 root 产物里
//   根本不存在 → 实测刷出 [live] [错误] 未知函数: ch_c3a8e36c4_hooks，并让 fulltest-game 的
//   game-chapter1 断言（errors=0）失败（2026-09-26 实测）。
//   现在统一走 ntl_mod_scripts_load()：按 working_directory 推断真实章节 + 幂等（同一目录不会重复加载）。
//   ⚠ 必须保留在 live_init 里：热重载 ntl_live_reload() 就是靠 ntl_live_init() 重建整张表来刷新 mods/ 脚本的。
var _fromMods = 0;
try { _fromMods = ntl_mod_scripts_load(); }
catch (e2) { ntl_log("live", "[错误] ntl_mod_scripts_load 异常: " + string(e2)); }

ntl_log("live", "live 运行时: " + string(_loaded) + " 个（live/） + " + string(_fromMods) + " 个（mods/）");

// ---------- 第 3 部分：执行 on_init ----------
ntl_live_emit("on_init", 0);
return _loaded + _fromMods;
