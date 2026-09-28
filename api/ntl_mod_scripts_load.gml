/// ntl_mod_scripts_load() —— 加载 mods/ 下所有 mod 自己的 Lua 脚本
/// ★ 这一步是 **mod 互操作** 的前提：没有它，mod 的 main.lua 根本不会跑。
///
/// 修复历史：
///   1) 章节检测原来用 while 数数字，结果得到 "chapter4_"(多下划线) → 改为直接用配置的章节号
///   2) 目录枚举原来只靠 file_find_first → 改为多级回退
///   3) 叶子目录原来按 [_chap, "root"] 加载（把 root 当通配符）→ 章节进程会加载 root 作用域
///      的 mod 脚本，而 root 叶子的 live 桥只编译进 root 产物 → `[错误] 未知函数: <slug>_hooks`。
///      现改为只加载当前目标 [_chap]：root 是目标名，不是「对所有章节生效」（见下面 :66 的注释）。
///   4) 作用域来源以前只靠路径猜（时间线产物猜不出 → 退回 config.auto_chapter，而它从不随产物
///      更新）→ 现在优先读部署时写入本产物的 Neutraled/scope.json（ntl_product_scope），
///      路径与 config 只作兜底。
var _root = program_directory + "Neutraled/mods/";
ntl_log("live", "[mod脚本] 扫描根目录: " + _root);

if (!directory_exists(_root))
{
    ntl_log("live", "[mod脚本] 目录不存在，跳过");
    return 0;
}
if (!variable_global_exists("ntl_live_mods")) global.ntl_live_mods = ds_list_create();

// ---- 当前作用域：**优先读产物清单**（唯一不靠猜的来源），其次按路径推断，最后才退回 config ----
//   ⚠ 旧写法只按 working_directory 里的数字猜：时间线产物的目录名
//     ntl_timeline_9_ntl_chapter_c3a8e36c4_c3a8e36c4 里 "chapter" 后面是下划线 → 猜不出来 →
//     退回 config.auto_chapter，而 auto_chapter 从不随产物更新 ⇒ 载入别的产物的 mod 脚本
//     （桥函数不在本产物 → [live] [错误] 未知函数: <slug>_hooks），auto_chapter=0 时则一个都不载入。
//     2026-09-26 审计 + 真机日志实测。
var _chap = ntl_product_scope();
var _chapFrom = (_chap != "") ? "产物 scope.json" : "";
if (_chap == "")
{
    var _wd = string_lower(string(working_directory));
    var _ci = string_pos("chapter", _wd);
    if (_ci > 0)
    {
        var _p = _ci + 7;
        var _num = "";
        while (_p <= string_length(_wd))
        {
            var _ch = string_copy(_wd, _p, 1);
            if (string_pos(_ch, "0123456789") <= 0) break;
            _num += _ch;
            _p += 1;
        }
        if (_num != "") { _chap = "chapter" + _num; _chapFrom = "working_directory 推断"; }
    }
}
if (_chap == "")
{
    if (ntl_is_root() == 1) { _chap = "root"; _chapFrom = "ntl_is_root()"; }
    else if (variable_global_exists("ntl_cfg"))
    {
        var _cfgCh = ds_map_find_value(global.ntl_cfg, "auto_chapter");
        if (_cfgCh > 0) { _chap = "chapter" + string(_cfgCh); _chapFrom = "config.auto_chapter（兜底）"; }
    }
}
if (_chapFrom == "") _chapFrom = "无（外部章节 / 未部署产物）";
ntl_log("live", "[mod脚本] 当前章节: " + _chap + "（来源: " + _chapFrom + "）");

// ---- 枚举 mods/<mod>/<author>/<chapter>/ ----
var _added = 0;
var _dupSkipped = 0;
var _names = [];

var _modNames = ntl_dir_list(_root);
ntl_log("live", "[mod脚本] mods/ 下有 " + string(array_length(_modNames)) + " 个条目");

for (var _mi = 0; _mi < array_length(_modNames); _mi += 1)
{
    var _md = _root + _modNames[_mi] + "/";
    if (!directory_exists(_md)) continue;
    var _authors = ntl_dir_list(_md);

    for (var _ai = 0; _ai < array_length(_authors); _ai += 1)
    {
        var _ad = _md + _authors[_ai] + "/";
        if (!directory_exists(_ad)) continue;

        // ★ 只加载**当前目标**的叶子目录（精确匹配，和 builder 一致）。
        //   `root` 是一个**目标名**（根/章节选择器产物），**不是**「对所有章节生效」的通配符：
        //     - builder/Mods.cs:454-456 IsChapterFolder 只认 `root` 与 `chapter*`；
        //     - builder/Mods.cs:357-359 按 `effChapter == chapter` 精确匹配决定部署到哪个产物。
        //   所以 `mods/<mod>/<author>/root/` 下的 gml（含 live 桥 <slug>_hooks）只存在于 root 产物里。
        //   以前这里写的是 [_chap, "root"] → 章节进程也会加载 root 作用域的 mod 脚本，
        //   桥函数在章节产物里不存在 → `[live] [错误] 未知函数: <slug>_hooks`（2026-09-26 真机复现）。
        var _tryChaps = [_chap];
        for (var _ci2 = 0; _ci2 < array_length(_tryChaps); _ci2 += 1)
        {
            var _cd = _ad + _tryChaps[_ci2] + "/";
            var _mjPath = _cd + "mod.json";
            if (!file_exists(_mjPath)) continue;

            // 重复加载守卫：同一个 mod 叶子（同一目录）只进表一次。
            //   ntl_live_init() 与 Step_1 第 2 帧都会调用本函数，热重载也会重跑；
            //   以前没有这个守卫 → 章节进程里同一个 mod 进表两次（on_init 多跑、hook 重复注册）。
            var _dup = 0;
            for (var _di = 0; _di < ds_list_size(global.ntl_live_mods); _di += 1)
            {
                var _de = ds_list_find_value(global.ntl_live_mods, _di);
                if (ds_map_find_value(_de, "dir") == _cd) { _dup = 1; break; }
            }
            if (_dup == 1) { _dupSkipped += 1; continue; }

            var _mj = undefined;
            try { _mj = json_parse(ntl_live_file_read(_mjPath)); } catch (e) { ntl_log("live", "[错误] mod.json 解析失败（跳过）: " + _mjPath + " → " + string(e)); continue; }
            if (_mj == undefined) continue;
            if (!variable_struct_exists(_mj, "scripts")) continue;

            // enabled: false 跳过
            if (variable_struct_exists(_mj, "enabled"))
            {
                var _en = variable_struct_get(_mj, "enabled");
                if (_en == false) continue;
            }

            var _entry = ds_map_create();
            var _modId = variable_struct_exists(_mj, "id") ? string(variable_struct_get(_mj, "id")) : _modNames[_mi];
            ds_map_add(_entry, "name", _modId);
            ds_map_add(_entry, "dir", _cd);
            ds_map_add(_entry, "env", ntl_e_new_env());
            ds_map_add(_entry, "hooks", ds_map_create());
            ds_map_add(_entry, "cache", ds_map_create());
            ds_map_add(_entry, "astcache", ds_map_create());
            ds_map_add(_entry, "from_mods", 1);

            var _scripts = _mj.scripts;
            var _skeys = variable_struct_get_names(_scripts);
            for (var _sk = 0; _sk < array_length(_skeys); _sk += 1)
            {
                var _ev = _skeys[_sk];
                var _file = string(variable_struct_get(_scripts, _ev));
                ds_map_add(ds_map_find_value(_entry, "hooks"), _ev, _file);
            }
            ds_list_add(global.ntl_live_mods, _entry);
            array_push(_names, _modId + "/" + _tryChaps[_ci2]);
            _added += 1;
        }
    }
}

if (_added > 0)
    ntl_log("live", "[mod脚本] 已加载 " + string(_added) + " 个: " + ntl_string_join_ext(", ", _names));
else if (_dupSkipped > 0)
    ntl_log("live", "[mod脚本] " + string(_dupSkipped) + " 个已加载过（重复加载跳过，避免重复注册 hook）");
else
    ntl_log("live", "[mod脚本] 没有找到可加载的 mod 脚本（检查 mods/<mod>/<author>/" + _chap + "/mod.json）");
return _added;
