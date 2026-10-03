# -*- coding: utf-8 -*-
"""从 pct 层（percentage / color）的存档菜单补丁里移除 "Color Filter" 按钮。

为什么需要这个脚本：
  pctobj 层会用反编译出的 DEVICE_MENU_Draw_0 / DEVICE_MENU_Step_0 **整脚本覆盖**官方对象，
  于是第 1-5 章的存档菜单会多出 pct 自己加的一行 "Color Filter"（日文下是 カラーフィルター），
  并且坐标 8 被塞进导航环。用户要求这一行**不要存在**，所以这里把：
    * Draw：CF 文本/宽度/挤压布局那一段清空（菜单回到官方布局），绘制点画空串，
            导航用的 == 8 心跳分支改成不可达；
    * Step：把坐标 8 从非控制台（普通版）导航与确认键里摘掉，非控制台侧的目标改成
            可见行（3/5/7/9），**保留** global.is_console 那套（8 = 退出程序 ossafe_game_end）。
  保留：pct 的标题 tagline（scr_cf_draw_device_tagline）、shader、obj_colorfiltermenu 本体、
        scr_cf_* 全部脚本（只是入口不再可达，pct 的颜色滤镜机制仍随层加载）。

两种写法：
  * ch1：MENUCOORD[0]，== 8 一律是 CF；
  * ch2-5：MENUCOORD[M]，且分 !global.is_console（8 = CF）/ global.is_console（8 = 退出程序）两套。

用法：
  python strip_color_filter_button.py            # 只报告（dry-run）
  python strip_color_filter_button.py --apply    # 真正写回
  echo --diff                                     # 额外打印改动 hunk（改前/改后）
再抽层（--layer-from-base）后需要重跑一次，本脚本幂等。
"""
import os, re, sys, io

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
PROBE = os.path.join(ROOT, "mods", "pctobj", "probe")
MARK = "// [Neutraled] Color Filter 按钮已移除（_tools/strip_color_filter_button.py）"
APPLY = "--apply" in sys.argv
DIFF = "--diff" in sys.argv

def strip_literals(s):
    """去掉字符串字面量，避免其中的花括号干扰计数。"""
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', s)

def brace_delta(s):
    t = strip_literals(s)
    return t.count("{") - t.count("}")

def find_enclosing_block(lines, anchor_i, header):
    """从 anchor_i 往上找 header（如 'if (!global.is_console)'）所在行，再配平括号找块尾。"""
    for i in range(anchor_i, -1, -1):
        if lines[i].strip() == header and i + 1 < len(lines) and lines[i + 1].strip() == "{":
            depth = 0
            for j in range(i + 1, len(lines)):
                depth += brace_delta(lines[j])
                if depth == 0:
                    return (i + 1, j)   # 花括号行, 花括号行
    return None

def print_hunks(old_txt, new_txt):
    a, b = old_txt.split("\n"), new_txt.split("\n")
    idx = [i for i in range(max(len(a), len(b)))
           if (a[i] if i < len(a) else None) != (b[i] if i < len(b) else None)]
    if not idx:
        return
    groups, cur = [], [idx[0]]
    for i in idx[1:]:
        if i - cur[-1] <= 4:
            cur.append(i)
        else:
            groups.append(cur); cur = [i]
    groups.append(cur)
    print("    改动 hunk %d 处：" % len(groups))
    for g in groups:
        lo, hi = max(0, g[0] - 2), min(len(a), g[-1] + 3)
        for i in range(lo, hi):
            mark = "  -" if i in g else "   "
            if i < len(a):
                print("      %s %4d| %s" % (mark, i + 1, a[i]))
        for i in range(lo, hi):
            if i in g and i < len(b):
                print("      + %4d| %s" % (i + 1, b[i]))

def apply_ops(path, ops, extra_checks=None):
    txt = io.open(path, encoding="utf-8", errors="replace").read()
    lines = txt.split("\n")
    before_bal = sum(brace_delta(l) for l in lines)
    report = []
    for name, fn in ops:
        lines, msg = fn(lines)
        report.append("    %-34s %s" % (name, msg))
    out = "\n".join(lines)
    after_bal = sum(brace_delta(l) for l in out.split("\n"))
    if before_bal != after_bal:
        report.append("    !! 括号平衡被破坏: %d -> %d（拒绝写入）" % (before_bal, after_bal))
        return False, report, txt
    if extra_checks:
        for name, fn in extra_checks:
            ok, msg = fn(out)
            report.append("    %-34s %s" % (name, msg))
            if not ok:
                return False, report, txt
    if APPLY and out != txt:
        io.open(path, "w", encoding="utf-8", newline="").write(out)
    return (out != txt), report, out

# ---------------- Draw ----------------
def draw_ops(already, mstyle):
    def op_blank_layout(lines):
        if already:
            return lines, "已是移除后状态，跳过"
        anchor = next((i for i, l in enumerate(lines) if 'menu_cf_text = "Color Filter";' in l), None)
        if anchor is None:
            return lines, "找不到 Color Filter 布局块（跳过）"
        blk = find_enclosing_block(lines, anchor, "if (!global.is_console)")
        if blk is None:
            return lines, "!! 找不到包裹它的 if (!global.is_console) 块"
        o, c = blk
        body = "\n".join(lines[o + 1:c])
        for need in ["Color Filter", "menu_cf_w", "menu_left_min", "menu_chapter_x"]:
            if need not in body:
                return lines, "!! 块内容不含 '%s'，拒绝改动" % need
        indent = re.match(r"[ \t]*", lines[o]).group(0) + "    "
        lines = lines[:o + 1] + [indent + MARK] + lines[c:]
        return lines, "清空 CF 布局块（原 %d 行 -> 1 行注释）" % (c - o - 1)

    def op_blank_draw(lines):
        if already:
            return lines, "已是移除后状态，跳过"
        hit = [i for i, l in enumerate(lines) if "draw_text_shadow(menu_cf_x, 190, menu_cf_text);" in l]
        if len(hit) != 1:
            return lines, "!! 绘制点出现 %d 次（期望 1）" % len(hit)
        i = hit[0]
        lines[i] = lines[i].replace('menu_cf_text);', '"");  ' + MARK)
        return lines, "绘制点改为画空串"

    def op_dead_heart(lines):
        if mstyle:
            want = "if (MENUCOORD[MENU_NO] == 8 && !global.is_console)"
            hit = [i for i, l in enumerate(lines) if l.strip() == want]
            for i in hit:
                lines[i] = lines[i].replace(want, "if (MENUCOORD[MENU_NO] == -1)")
            return lines, "CF 心跳分支 == 8 -> == -1（%d 处，控制台心跳保留）" % len(hit)
        hit = [i for i, l in enumerate(lines) if l.strip() == "if (MENUCOORD[MENU_NO] == 8)"]
        if not hit:
            return lines, "心跳分支已不可达/不存在"
        for i in hit:
            lines[i] = lines[i].replace("== 8)", "== -1)")
        return lines, "心跳分支条件 == 8 -> == -1（%d 处）" % len(hit)

    return [("清空 CF 布局块", op_blank_layout), ("CF 绘制点", op_blank_draw), ("心跳分支", op_dead_heart)]

def draw_checks(mstyle):
    def c1(out):
        n = out.count('"Color Filter"') + out.count("カラーフィルター")
        return (n == 0), "残留 CF 文本字面量: %d" % n
    if mstyle:
        def c2(out):
            bad = out.count("MENUCOORD[MENU_NO] == 8 && !global.is_console")
            keep = out.count("MENUCOORD[MENU_NO] == 8 && global.is_console")
            return (bad == 0 and keep == 1), "CF 心跳残留 %d（应为 0）/ 控制台心跳 %d（应为 1）" % (bad, keep)
    else:
        def c2(out):
            n = out.count("MENUCOORD[MENU_NO] == 8")
            return (n == 0), "残留 MENUCOORD[MENU_NO] == 8: %d" % n
    return [("检查 CF 字面量", c1), ("检查心跳 == 8", c2)]

# ---------------- Step: ch1（MENUCOORD[0]）----------------
S_OPS = [
    ("down: 3||8 -> 3",
     r"else if \(MENUCOORD\[0\] == 3 \|\| MENUCOORD\[0\] == 8\)",
     "else if (MENUCOORD[0] == 3)"),
    ("up: 去掉 || 8",
     r"else if \(MENUCOORD\[0\] == 3 \|\| MENUCOORD\[0\] == 4 \|\| MENUCOORD\[0\] == 5 \|\| MENUCOORD\[0\] == 8\)",
     "else if (MENUCOORD[0] == 3 || MENUCOORD[0] == 4 || MENUCOORD[0] == 5)"),
    ("right: 删掉 8->3 分支",
     r"if \(MENUCOORD\[0\] == 8\)\s*\{\s*MOVENOISE = 1;\s*MENUCOORD\[0\] = 3;\s*\}\s*else if \(MENUCOORD\[0\] >= 3 && MENUCOORD\[0\] < 5\)",
     "if (MENUCOORD[0] >= 3 && MENUCOORD[0] < 5)"),
    ("right: 5 -> 3（不再进 CF）",
     r"if \(!global\.is_console\)\s*\{\s*MENUCOORD\[0\] = 8;\s*\}\s*else\s*\{\s*MENUCOORD\[0\] = 3;\s*\}",
     "MENUCOORD[0] = 3;"),
    ("left: 3 -> 5（不再进 CF）",
     r"if \(!global\.is_console\)\s*\{\s*MENUCOORD\[0\] = 8;\s*\}\s*else\s*\{\s*MENUCOORD\[0\] -= 1;\s*if \(MENUCOORD\[0\] < 3\)\s*\{\s*MENUCOORD\[0\] = 5;\s*\}\s*\}",
     "MENUCOORD[0] -= 1;\n                if (MENUCOORD[0] < 3)\n                {\n                    MENUCOORD[0] = 5;\n                }"),
    ("left: 6 -> 7（不再进 CF）",
     r"if \(!global\.is_console\)\s*\{\s*MENUCOORD\[0\] = 8;\s*\}\s*else\s*\{\s*MENUCOORD\[0\] = 7;\s*\}",
     "MENUCOORD[0] = 7;"),
    ("left: 死分支 == 8 -> == -1",
     r"else if \(MENUCOORD\[0\] == 8\)\s*\{\s*MOVENOISE = 1;\s*MENUCOORD\[0\] = 5;\s*\}",
     "else if (MENUCOORD[0] == -1)\n        {\n            MOVENOISE = 1;\n            MENUCOORD[0] = 5;\n        }"),
    ("console 守卫去掉 || 8",
     r"if \(MENUCOORD\[0\] == 7 \|\| MENUCOORD\[0\] == 8\)",
     "if (MENUCOORD[0] == 7)"),
    ("确认键: 8 分支不可达",
     r"if \(MENUCOORD\[0\] == 8\)(\s*\{\s*ONEBUFFER = 2;)",
     r"if (MENUCOORD[0] == -1)\1"),
]

def step_editor():
    def fn(lines):
        txt = "\n".join(lines)
        msgs = []
        for name, pat, rep in S_OPS:
            rx = re.compile(pat)
            n = len(rx.findall(txt))
            if n == 0:
                msgs.append("%-26s 0 次（已处理过或结构不同）" % name)
                continue
            if n > 1:
                msgs.append("!! %-24s 命中 %d 次（期望 1）" % (name, n))
                continue
            txt = rx.sub(lambda m: m.expand(rep) if "\\1" in rep else rep, txt, count=1)
            msgs.append("%-26s ok" % name)
        return txt.split("\n"), "; ".join(msgs)
    return [("Step 导航改写", fn)]

def step_checks():
    def c1(out):
        n = out.count("MENUCOORD[0] == 8") + len(re.findall(r"MENUCOORD\[0\] = 8;", out))
        return (n == 0), "残留 coord 8: %d" % n
    return [("检查 coord 8", c1)]

# ---------------- Step: ch2-5（MENUCOORD[M]，双模式）----------------
def kill_line(stripped_from, stripped_to):
    """把整行条件改成不可达（保留缩进），命中任意次都改。"""
    def fn(lines):
        hit = [i for i, l in enumerate(lines) if l.strip() == stripped_from]
        for i in hit:
            lines[i] = lines[i].replace(stripped_from, stripped_to)
        return lines, "%-30s %d 处" % ("不可达: " + stripped_from[:26], len(hit))
    return fn

M_OPS = [
    # 1) 非控制台的「8 -> ...」分支：坐标 8 不再存在，整条分支不可达
    ("down: 非控制台 8->6 不可达", kill_line(
        "else if (!global.is_console && MENUCOORD[M] == 8)",
        "else if (MENUCOORD[M] == -1)")),
    ("right: 非控制台 8->3 不可达", kill_line(
        "if (!global.is_console && MENUCOORD[M] == 8)",
        "if (MENUCOORD[M] == -1)")),
    ("left: 非控制台 8->3 不可达", kill_line(
        "else if (!global.is_console && MENUCOORD[M] == 8)",
        "else if (MENUCOORD[M] == -1)")),
    ("left: 非控制台 8->3 不可达（反向写法）", kill_line(
        "else if (MENUCOORD[M] == 8 && !global.is_console)",
        "else if (MENUCOORD[M] == -1)")),
    ("down: 8->9 分支不可达", kill_line(
        "else if (MENUCOORD[M] == 8 && !global.is_console && CANQUIT == 1)",
        "else if (MENUCOORD[M] == -1)")),
    ("right: 8->9 分支不可达", kill_line(
        "else if (!global.is_console && MENUCOORD[M] == 8 && CANQUIT == 1)",
        "else if (MENUCOORD[M] == -1)")),
    ("确认键: 8 开 CF 不可达", kill_line(
        "if (MENUCOORD[M] == 8 && !global.is_console)",
        "if (MENUCOORD[M] == -1)")),
    # 2) up 分支的多条件里去掉 8
    ("up: 条件去掉 || 8", kill_line(
        "else if (MENUCOORD[M] == 3 || MENUCOORD[M] == 4 || MENUCOORD[M] == 5 || MENUCOORD[M] == 7 || (!global.is_console && MENUCOORD[M] == 8))",
        "else if (MENUCOORD[M] == 3 || MENUCOORD[M] == 4 || MENUCOORD[M] == 5 || MENUCOORD[M] == 7)")),
]

def m_editor():
    """ch2-5：正则改写剩下的非控制台目标（保留 global.is_console 那一套）。"""
    def fn(lines):
        txt = "\n".join(lines)
        msgs = []
        for name, pat, rep in M_REGEX_OPS:
            rx = re.compile(pat)
            n = len(rx.findall(txt))
            if n == 0:
                msgs.append("%-26s 0 次（已处理过或结构不同）" % name)
                continue
            txt = rx.sub(lambda m: m.expand(rep), txt)
            msgs.append("%-26s ok×%d" % (name, n))
        return txt.split("\n"), "; ".join(msgs)
    return [("Step 目标改写", fn)]

# 每个正则都用 (?m)^([ \t]*) 抓基础缩进，替换里用 \1 复原（其余按 4 空格递进）
M_REGEX_OPS = [
    ("down: 7 -> 9（非控制台）",
     r"(?m)^([ \t]*)else if \(MENUCOORD\[M\] == 7\)\s*\n\1\{\s*\n\1    if \(!global\.is_console\)\s*\n\1    \{\s*\n\1        MENUCOORD\[M\] = 8;[ \t]*\n",
     r"\1else if (MENUCOORD[M] == 7)\n\1{\n\1    if (!global.is_console)\n\1    {\n\1        if (CANQUIT == 1)\n\1        {\n\1            MENUCOORD[M] = 9;\n\1        }\n"),
    ("right/left: 6 -> 7（非控制台）",
     r"(?m)^([ \t]*)else if \(MENUCOORD\[M\] == 6\)\s*\n\1\{\s*\n\1    if \(!global\.is_console\)\s*\n\1    \{\s*\n\1        MENUCOORD\[M\] = 8;",
     r"\1else if (MENUCOORD[M] == 6)\n\1{\n\1    if (!global.is_console)\n\1    {\n\1        MENUCOORD[M] = 7;"),
    ("left: 3 -> 5（非控制台）",
     r"(?m)^([ \t]*)if \(MENUCOORD\[M\] == 3\)\s*\n\1\{\s*\n\1    MOVENOISE = 1;\s*\n\1    if \(!global\.is_console\)\s*\n\1    \{\s*\n\1        MENUCOORD\[M\] = 8;",
     r"\1if (MENUCOORD[M] == 3)\n\1{\n\1    MOVENOISE = 1;\n\1    if (!global.is_console)\n\1    {\n\1        MENUCOORD[M] = 5;"),
    ("up/left: 9 -> 7（非控制台）",
     r"(?m)^([ \t]*)else if \(MENUCOORD\[M\] == 9\)\s*\n\1\{\s*\n\1    MENUCOORD\[M\] = 8;[ \t]*\n\1\}",
     r"\1else if (MENUCOORD[M] == 9)\n\1{\n\1    if (!global.is_console)\n\1    {\n\1        MENUCOORD[M] = 7;\n\1    }\n\1    else\n\1    {\n\1        MENUCOORD[M] = 8;\n\1    }\n\1}"),
]

def m_checks():
    def c1(out):
        hits = [i + 1 for i, l in enumerate(out.split("\n"))
                if "!global.is_console" in l and "== 8" in l]
        n = len(re.findall(r"if \(!global\.is_console\)\s*\n\s*\{\s*\n\s*MENUCOORD\[M\] = 8;", out))
        msg = "非控制台残留 == 8: %d（应 0" % len(hits)
        if hits:
            msg += "，行 %s" % ",".join(str(x) for x in hits[:12])
        msg += "）/ 非控制台赋值 8: %d（应 0）" % n
        return (not hits and n == 0), msg
    def c2(out):
        ok = ("ossafe_game_end()" in out) and ("game_end();" in out) and out.count("MENUCOORD[M] = 8;") >= 1
        return ok, "控制台退出路径 intact: %s（剩余赋值 8: %d）" % (ok, out.count("MENUCOORD[M] = 8;"))
    return [("检查 coord 8（非控制台）", c1), ("检查控制台路径", c2)]

def main():
    print("模式: %s%s" % ("APPLY（写回）" if APPLY else "dry-run（只报告）", " + diff" if DIFF else ""))
    total = 0
    for ch in range(1, 6):
        d = os.path.join(PROBE, "chapter%d" % ch, "patches")
        for kind in ("Draw_0", "Step_0"):
            p = os.path.join(d, "gml_Object_DEVICE_MENU_%s.gml" % kind)
            if not os.path.exists(p):
                print("!! 缺文件 %s" % p); continue
            txt = io.open(p, encoding="utf-8", errors="replace").read()
            already = MARK in txt
            if kind == "Draw_0":
                mstyle = "MENUCOORD[MENU_NO] == 8 && " in txt
                changed, rep, out = apply_ops(p, draw_ops(already, mstyle), draw_checks(mstyle))
                tag = "M" if mstyle else "0"
            else:
                mstyle = "MENUCOORD[M]" in txt
                if mstyle:
                    ops = [(n, f) for n, f in M_OPS] + m_editor()
                    changed, rep, out = apply_ops(p, ops, m_checks())
                else:
                    changed, rep, out = apply_ops(p, step_editor(), step_checks())
                tag = "M" if mstyle else "0"
            print("chapter%d %s [%s] %s" % (ch, kind, tag, "（有改动）" if changed else "（无改动/被拒）"))
            for r in rep:
                print(r)
            if DIFF and changed:
                print_hunks(txt, out)
            total += 1 if changed else 0
    print("完成：%d 个文件%s" % (total, "已写回" if APPLY else "需要改动（未写回，加 --apply 生效）"))

main()
