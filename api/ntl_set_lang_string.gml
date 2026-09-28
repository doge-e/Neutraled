// ntl_set_lang_string(key, text) —— 注册文本覆盖并**立刻**写入 global.lang_map
// 旧行为：只写 global.ntl_lang_overrides，等 Step 里那次 ntl_apply_lang_overrides 才生效；
//         而那次只在第 2 帧跑一遍 —— 晚于它的 mod（热重载/late on_init）永远改不到游戏文字。
var _key = string(argument[0]);
var _txt = (argument_count > 1) ? string(argument[1]) : "";
if (!variable_global_exists("ntl_lang_overrides")) global.ntl_lang_overrides = ds_map_create();
ds_map_replace(global.ntl_lang_overrides, _key, _txt);

// ★ F5 接线：立刻生效（覆盖优先于 lang_map，见 ntl_get_lang_string）
var _applied = 0;
try
{
    if (variable_global_exists("lang_map"))
    {
        if (ds_map_exists(global.lang_map, _key)) ds_map_replace(global.lang_map, _key, _txt);
        else ds_map_add(global.lang_map, _key, _txt);
        _applied = 1;
    }
    if (variable_global_exists("lang_missing_map") && ds_map_exists(global.lang_missing_map, _key))
        ds_map_delete(global.lang_missing_map, _key);
}
catch (e) { ntl_log("i18n", "[ntl] ntl_set_lang_string.gml 应用失败: " + string(e)); }

ntl_log("i18n", "[覆盖] " + _key + " = " + _txt + "（已写入 lang_map=" + string(_applied) + "）");
return 1;
