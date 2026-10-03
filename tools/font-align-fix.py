# -*- coding: utf-8 -*-
"""把 Ark 字形在裁剪块内的墨迹整体下移，使其基线与游戏自带 fnt_main（8bitoperator JVE）一致。

背景（2026-10-02 取证）：部署期 FontNative 会把本机 data.win 的 fnt_main 字形覆盖进字体包副本
（latin 区 0x20-0x7E + 0xA0-0x17F），凡是游戏字体里有的码位都用游戏字形；游戏字体没有的
（ä ü ß ç é ñ ā … 等 Latin-1/Ext-A 带声调字）保留我们 Ark 渲染的字形 —— 而 Ark 字形
当年由 ImageMagick label: 渲染，墨迹底在裁剪块第 9 行，游戏自己的拉丁字形墨迹底在第 12 行
（M1：屏幕行 = 行顶 + 块内墨迹行）⇒ 屏幕上高 3 px，这就是用户说的「修复的字向上偏移」。

做法：整块下移 N 行（N 由本机字形与包内字形的墨迹底中位数差实测得出，默认 3），
裁剪块高度只增不减（M1 证明 SH 不产生垂直位移，安全）。
"""
import json, os, shutil, statistics, sys, collections
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
FONTS = os.environ.get("NTL_FONTS", os.path.join(ROOT, "fonts"))
PACK = os.path.join(FONTS, "ntl_font_cjk.json")
ARK = "ntl_font_cjk_ark12.png"
NATIVE = os.environ.get("NTL_NATIVE_DIR", os.path.join(ROOT, ".tmp", "font-native-src", "root", "game"))
NAT_JSON = os.path.join(NATIVE, "ntl_native_game.json")
NAT_PNG = os.path.join(NATIVE, "ntl_native_game_page.png")
BACKUP = os.environ.get("NTL_ALIGN_BACKUP", os.path.join(ROOT, ".tmp", "pack-backup-align"))
SHEET_W, SHEET_H = 4096, 2048
APPLY = "--apply" in sys.argv

def ink(al, x, y, w, h):
    bb = al.crop((x, y, x + w, y + h)).getbbox()
    return bb  # (l,t,r,b) 相对裁剪块；None = 空

pk = json.load(open(PACK, encoding="utf-8"))
gl = [g for g in pk["Glyphs"] if g["Sheet"] == ARK]
sheet = Image.open(os.path.join(FONTS, ARK)).convert("RGBA")
al = sheet.split()[3]
print("Ark 字形 %d 个，页 %s" % (len(gl), sheet.size))

old = {}
hist_old = collections.Counter()
for g in gl:
    bb = ink(al, g["SX"], g["SY"], g["SW"], g["SH"])
    old[g["Char"]] = bb
    if bb: hist_old[bb[3] - 1] += 1
print("旧墨迹底直方图（行号:字形数）:", dict(sorted(hist_old.items())))

# ---- 本机（游戏自带）字形墨迹底 ----
nat = {}
if os.path.exists(NAT_JSON) and os.path.exists(NAT_PNG):
    nj = json.load(open(NAT_JSON, encoding="utf-8"))
    nal = Image.open(NAT_PNG).convert("RGBA").split()[3]
    for g in nj["Glyphs"]:
        bb = ink(nal, g["SX"], g["SY"], g["SW"], g["SH"])
        if bb: nat[g["Char"]] = bb
    shared = sorted(set(nat) & {g["Char"] for g in gl if old[g["Char"]]})
    deltas = collections.Counter()
    for cp in shared:
        deltas[(nat[cp][3] - 1) - (old[cp][3] - 1)] += 1
    print("本机字形 %d 个，与本包共有 %d 个；需要的下移量直方图:" % (len(nat), len(shared)))
    for k, v in sorted(deltas.items()):
        print("   下移 %+d 行: %d 个" % (k, v))
    need = statistics.median([(nat[cp][3] - 1) - (old[cp][3] - 1) for cp in shared])
    print("实测中位数下移量 = %s" % need)
else:
    need = 3
    print("!! 找不到本机字形源，使用默认下移 3 行")

SHIFT = int(round(need))
SAMPLE = [0x41, 0x61, 0x67, 0x79, 0xE4, 0xFC, 0xDF, 0xE7, 0xC4, 0xE9, 0xF1, 0xAB, 0xA9, 0xB2, 0x3B1, 0x434, 0x2192]
print("用例（码位 旧墨迹行 -> 新墨迹行 / 本机行）:")
for cp in SAMPLE:
    g = next((q for q in gl if q["Char"] == cp), None)
    if not g: print("   U+%04X 不在包内" % cp); continue
    bb = old[cp]
    if not bb: print("   U+%04X 空字形" % cp); continue
    nt = "" if cp not in nat else " 本机底=%d" % (nat[cp][3] - 1)
    print("   U+%04X %-3s 旧 %d..%d -> 新 %d..%d%s" % (cp, chr(cp), bb[1], bb[3] - 1, bb[1] + SHIFT, bb[3] - 1 + SHIFT, nt))

if not APPLY:
    print("（未加 --apply，仅报告）")
    sys.exit(0)

# ---------- 备份 ----------
os.makedirs(BACKUP, exist_ok=True)
for f in ["ntl_font_cjk.json", ARK]:
    dst = os.path.join(BACKUP, f)
    if not os.path.exists(dst): shutil.copy2(os.path.join(FONTS, f), dst)
print("备份 ->", BACKUP)

# ---------- 重建页 ----------
new = Image.new("RGBA", (SHEET_W, SHEET_H), (0, 0, 0, 0))
cx = 0; cy = 0; rowH = 0
moved = 0; grew = 0
for g in gl:
    bb = old[g["Char"]]
    w = g["SW"]; sh_old = g["SH"]
    if bb is None:                       # 空字形（如空格）：原样搬运
        crop = sheet.crop((g["SX"], g["SY"], g["SX"] + w, g["SY"] + sh_old))
        h = sh_old
    else:
        it, ib = bb[1], bb[3] - 1        # 墨迹行（块内）
        need_h = ib + SHIFT + 1          # 下移后墨迹底所在行 + 1
        h = max(sh_old, need_h)
        if h > sh_old: grew += 1
        crop = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        piece = sheet.crop((g["SX"] + bb[0], g["SY"] + it, g["SX"] + bb[2], g["SY"] + ib + 1))
        crop.paste(piece, (bb[0], it + SHIFT))
        moved += 1
    if cx + w > SHEET_W:
        cx = 0; cy += rowH + 1; rowH = 0
    if cy + h > SHEET_H:
        raise SystemExit("页放不下：cy=%d h=%d" % (cy, h))
    new.paste(crop, (cx, cy))
    g["SX"] = cx; g["SY"] = cy; g["SW"] = w; g["SH"] = h
    cx += w + 1
    if h > rowH: rowH = h
print("重排完成：移动 %d 个，加高 %d 个，占用高度 %d 行" % (moved, grew, cy + rowH))

new.save(os.path.join(FONTS, ARK), optimize=True)
json.dump(pk, open(PACK, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
print("已写回 %s + %s（%d 字节）" % (PACK, ARK, os.path.getsize(os.path.join(FONTS, ARK))))

# ---------- 自检 ----------
chk = Image.open(os.path.join(FONTS, ARK)).convert("RGBA").split()[3]
bad = []
hist_new = collections.Counter()
gl2 = [g for g in json.load(open(PACK, encoding="utf-8"))["Glyphs"] if g["Sheet"] == ARK]
for g in gl2:
    bb = ink(chk, g["SX"], g["SY"], g["SW"], g["SH"])
    if bb is None: continue
    hist_new[bb[3] - 1] += 1
    o = old.get(g["Char"])
    if o is None: bad.append((g["Char"], "无旧记录")); continue
    if (bb[3] - 1) != (o[3] - 1) + SHIFT: bad.append((g["Char"], "底 %d 应为 %d" % (bb[3] - 1, o[3] - 1 + SHIFT)))
    if bb[0] != o[0] or bb[1] != o[1] + SHIFT: bad.append((g["Char"], "左/上行对不上"))
print("自检：新墨迹底直方图", dict(sorted(hist_new.items())))
print("自检：异常 %d 个 %s" % (len(bad), bad[:8]))
