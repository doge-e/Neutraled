/// ntl_map_test() —— 最小闭环测试：加载一张 Kristal 地图并显示状态
if (file_exists(program_directory + "Neutraled/kristal-maps/before_palace.png"))
{
    var _mp = ntl_map_load("before_palace");
    if (ds_map_find_value(_mp, "ok") == 1)
    {
        ntl_log("map", "最小闭环: 地图已加载 " +
                string(ds_map_find_value(_mp, "width")) + "x" + string(ds_map_find_value(_mp, "height")) +
                " 碰撞块 " + string(array_length(ds_map_find_value(_mp, "collision"))));
        global.ntl_test_map = _mp;
    }
}
return 0;
