#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
restore_menu_button_positions.py  (Neutraled _tools)

背景
----
PERCENTAGE(Color Filter) mod 的源码级差异层(pctobj)为了让出「Color Filter」按钮的位置，
把存档界面底部的三个按钮从官方的固定坐标改成了「右对齐簇」：

    menu_chapter_x = 204;
    if ((menu_chapter_x + menu_chapter_w) > menu_right_max)
    {
        menu_chapter_x = menu_right_max - menu_chapter_w;
    }
    menu_erase_x = menu_chapter_x - menu_row_pad - menu_erase_w;
    menu_copy_x = menu_erase_x - menu_row_pad - menu_copy_w;

于是「复制」被推到右边（中文字体下 54 -> ~138），用户主诉「存档界面的复制按钮应当回到原位」。

Color Filter 按钮已经被 _tools/strip_color_filter_button.py 移除，这段重算不再需要：
删掉它以后 menu_copy_x / menu_erase_x / menu_chapter_x 就保持初始化值
    54 / 140 / 204  （与官方 data.win 的 draw_text_shadow(54/140/204, 190, ...) 完全一致）

用法
----
    python restore_menu_button_positions.py            # dry-run（默认，只报告）
    python restore_menu_button_positions.py --apply    # 真正写回

幂等：已经处理过的文件会被报告为「已是恢复后状态」，不会重复改动。
安全校验：删除后检查 (a) 花括号平衡不变 (b) 不再残留
    "menu_erase_x = menu_chapter_x" / "menu_copy_x = menu_erase_x"
否则拒绝写入该文件。
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # Neutraled\
PATCH_REL = os.path.join("mods", "pctobj", "probe", "chapter%d", "patches",
                         "gml_Object_DEVICE_MENU_Draw_0.gml")

MARK = ("// [Neutraled] 已恢复官方按钮原位（复制 54 / 消除 140 / 选择章节 204）："
        "Color Filter 按钮移除后不再需要右移这段推导（_tools/restore_menu_button_positions.py）")

# 待删除的块（allow 前后缩进差异；点号用 \. 无特殊字符，直接转义括号）
BLOCK_RE = re.compile(
    r"(?m)^([ \t]*)menu_chapter_x = 204;[ \t]*\r?\n"
    r"[ \t]*if \(\(menu_chapter_x \+ menu_chapter_w\) > menu_right_max\)[ \t]*\r?\n"
    r"[ \t]*\{[ \t]*\r?\n"
    r"[ \t]*menu_chapter_x = menu_right_max - menu_chapter_w;[ \t]*\r?\n"
    r"[ \t]*\}[ \t]*\r?\n"
    r"[ \t]*menu_erase_x = menu_chapter_x - menu_row_pad - menu_erase_w;[ \t]*\r?\n"
    r"[ \t]*menu_copy_x = menu_erase_x - menu_row_pad - menu_copy_w;[ \t]*\r?\n"
)

RESIDUAL_A = "menu_erase_x = menu_chapter_x"
RESIDUAL_B = "menu_copy_x = menu_erase_x"


def brace_balance(txt):
    return txt.count("{") - txt.count("}")


def process(path, apply):
    if not os.path.isfile(path):
        return ("MISSING", "文件不存在")
    with open(path, "r", encoding="utf-8", newline="") as f:
        src = f.read()
    if MARK in src:
        return ("ALREADY", "已是恢复后状态（含标记）")
    m = BLOCK_RE.search(src)
    if not m:
        return ("NOMATCH", "没有找到重算块（结构可能已变，人工检查）")
    indent = m.group(1)
    repl = indent + MARK + ("\r\n" if "\r\n" in src else "\n")
    out = src[:m.start()] + repl + src[m.end():]

    # 校验
    if brace_balance(out) != brace_balance(src):
        return ("REJECT", "花括号不平衡（%d -> %d）" % (brace_balance(src), brace_balance(out)))
    if RESIDUAL_A in out or RESIDUAL_B in out:
        return ("REJECT", "仍残留重算赋值")
    # 必须仍然保留三个初始化常量
    missing = [v for v in ("var menu_copy_x = 54;", "var menu_erase_x = 140;", "var menu_chapter_x = 204;")
               if v not in out]
    if missing:
        return ("REJECT", "初始化常量缺失: %s" % ", ".join(missing))
    if not apply:
        return ("WOULD", "将删除 %d 行重算块 -> 恢复 54/140/204" % (m.group(0).count("\n")))
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(out)
    return ("WROTE", "已删除重算块 -> 恢复 54/140/204")


def main():
    apply = "--apply" in sys.argv
    print("Neutraled _tools/restore_menu_button_positions.py  (%s)" % ("APPLY" if apply else "DRY-RUN"))
    print("项目根: %s" % ROOT)
    bad = 0
    for n in range(1, 6):
        path = os.path.join(ROOT, PATCH_REL % n)
        status, detail = process(path, apply)
        flag = "OK " if status in ("WROTE", "WOULD", "ALREADY") else "!! "
        if status in ("MISSING", "NOMATCH", "REJECT"):
            bad += 1
        print("  %schapter%d: [%s] %s" % (flag, n, status, detail))
    print("结果: %s" % ("存在问题，未全部处理" if bad else "全部 5 章就绪"))


if __name__ == "__main__":
    main()
