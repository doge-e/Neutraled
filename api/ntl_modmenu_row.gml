/// ntl_modmenu_row(i) —— 当前视图第 i 行的数据：[标签, 数值, 说明, 变灰, kind, action]
/// 行列表的唯一来源是 ntl_modmenu_rows()（顺序/文案/数量只定义在那里，改行不会漏改绘制或输入）；
/// 本函数只负责按下标取一行。越界返回空数组。
/// 老调用方只读前 4 个元素（标签/数值/说明/变灰），新增的 kind/action 在末尾，向后兼容。
var _rows = ntl_modmenu_rows();
var _i = real(argument[0]);
if (_i < 0 || _i >= array_length(_rows)) return [];
return _rows[_i];
