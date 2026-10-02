# 主题与语言（THEMES & LANGS）

> **本篇包含**（按顺序）：
> - [主题与语言：换配色与换界面语言](#主题与语言换配色与换界面语言) — 主题：换配色
> - [Neutraled 中英双语](#neutraled-中英双语) — 中英双语与外部语言包

## 主题与语言：换配色与换界面语言

> 一句话：这一篇解决「控制台太刺眼/字太小怎么换成别的配色」和「怎么让 Neutraled 与游戏内界面说中文以外的话（含自己翻译一种新语言）」。
>
> 主题只改**配色**，不改功能；语言只改**文案**，不改行为。两者互不影响，都记在 `Neutraled/config.json` 里（`theme` 与 `lang`）。总览见 [MANAGE.md](MANAGE.md) §8/§9；中英双语表本身的维护规则见 [THEMES-LANGS.md](THEMES-LANGS.md)。

- 主题：`--theme-list` / `--theme-show` / `--theme-use` / `--theme-import`
- 语言：`--lang-list` / `--lang-use` / `--lang-coverage` / `--lang-template`
- 数据：`Neutraled/themes/<id>.json`（外部主题）、`Neutraled/lang/lang_<code>.json`（外部语言包）、`Neutraled/console-theme.json`（给游戏读的配色）

命令里的 `$exe` 指：

```powershell
$exe = "E:\steam\steamapps\common\DELTARUNE\Neutraled\builder\bin\Release\net9.0\ntl-builder.exe"
```

**每条这类命令都会先打印两行公共前缀**（下面不再重复贴）。本文抓输出时 `Neutraled/config.json` 的 `lang` 是 `zh`，所以前缀与日志是中文；这个文件里的 `lang` 决定 builder 自己的打印语言（实测设成 `ru` 会连 builder 的输出一起变俄语）：

```text
游戏根: E:\steam\steamapps\common\DELTARUNE
[语言] 已加载 7 个外部语言包: de, es, fr, ja, ko, ru, zh-tw（共 15715 条译文）
```

## 一、主题

主题是一组**颜色字符串**（`#RRGGBB`），控制台的每一块颜色都从主题里取。内置 4 套，永远可用；你也可以放自己的主题文件进去。

### 1. 看有哪些主题

```powershell
& $exe --theme-list
```

```text
===== 主题 =====
  * dark                 深色（默认）               内置
    light                浅色                   内置
    high-contrast        高对比度（无障碍）            内置
    classic-deltarune    经典 DELTARUNE         内置
  共 4 个主题（内置 4，外部 0）
  活动主题: dark
  外部主题目录: E:\steam\steamapps\common\DELTARUNE\Neutraled\themes
```

行首的 `*` 表示当前活动主题。列表本身也吃 `--lang`，例如 `--theme-list --lang en` 会输出 `Themes` / `built-in` / `Active theme: dark`。

### 2. 看某个主题的完整定义

```powershell
& $exe --theme-show dark
```

它直接打印这个主题的 JSON（缩进 2 空格），**就是可以直接回灌 `--theme-import` 的格式**：

```json
{
  "schema": 1,
  "id": "dark",
  "name": "深色（默认）",
  "author": "Neutraled",
  "description": "Neutraled 默认深色配色（黑底蓝调，久看不累）",
  "colors": {
    "bg": "#101014",
    "panel": "#16161e",
    "fg": "#e6e6e6",
    "dim": "#8a8a96",
    "border": "#2a2a38",
    "accent": "#4fc3f7",
    "highlight": "#ffd54f",
    "selection": "#2f5fd0",
    "ok": "#7ee081",
    "warn": "#ffb74d",
    "error": "#ff6b6b"
  }
}
```

（`builtIn` 不落盘：内置标记是运行时算出来的，写进文件也会被忽略。）

⚠️ **主题不存在时也返回 0**：

```powershell
& $exe --theme-show nosuchtheme
# [主题] 找不到主题: nosuchtheme
# → 退出码 0（脚本里别只看退出码）
```

### 3. 内置四套主题的配色

颜色键的顺序固定为 `bg, panel, fg, dim, border, accent, highlight, selection, ok, warn, error`：

| 键 | 用在哪里 | dark（深色，默认） | light（浅色） | high-contrast（高对比度） | classic-deltarune（经典） |
|---|---|---|---|---|---|
| `bg` | 面板底色 | `#101014` | `#f5f5f7` | `#000000` | `#000000` |
| `panel` | 内容区底色 | `#16161e` | `#ffffff` | `#000000` | `#101010` |
| `fg` | 正文 | `#e6e6e6` | `#1c1c22` | `#ffffff` | `#ffffff` |
| `dim` | 次要文字 | `#8a8a96` | `#6b6b78` | `#c0c0c0` | `#a0a0a0` |
| `border` | 分隔线 | `#2a2a38` | `#d0d0d8` | `#ffffff` | `#555555` |
| `accent` | 标题与强调 | `#4fc3f7` | `#0b63c5` | `#00e5ff` | `#c0a0ff` |
| `highlight` | `==` 高亮行 | `#ffd54f` | `#e6a700` | `#ffff00` | `#ffff80` |
| `selection` | 过滤中/选中项 | `#2f5fd0` | `#bcd7ff` | `#0050d0` | `#404080` |
| `ok` | 输入行与成功 | `#7ee081` | `#2e7d32` | `#00ff00` | `#80ff80` |
| `warn` | `[警告]` | `#ffb74d` | `#b26a00` | `#ffaa00` | `#ffcc44` |
| `error` | `[错误]` | `#ff6b6b` | `#c62828` | `#ff0000` | `#ff5555` |

### 4. 导入自己的主题

「以内置主题为模板：导出一份 → 改颜色 → 导回」是最省事的路子，但**不能直接把它重定向进文件**（实测踩坑，见下）：

```powershell
# ① 导出（注意：输出里前两行是公共前缀，JSON 从第一个 { 开始）
& $exe --theme-show dark > dark-dump.txt
# ② 编辑器里删掉前两行「游戏根: …」「[语言] …」，把剩下的 JSON 另存为 UTF-8（无 BOM）
#    然后把 "id": "dark" 改成你自己的 id，例如 "id": "my-theme"
& $exe --theme-import my-theme.json
# [主题] 已导入: my-theme（我的主题）

& $exe --theme-list
# （多出一行）    my-theme             我的主题                    外部
#   共 5 个主题（内置 4，外部 1）

& $exe --theme-use my-theme
```

**为什么要手工删那两行**（实测）：直接

```powershell
& $exe --theme-show dark > my-theme.json
& $exe --theme-import my-theme.json
```

会失败：

```text
[主题] 导入失败：JSON 解析错误 → '0xE6' is an invalid start of a value. Path: $ | LineNumber: 0 | BytePositionInLine: 0.
```

两个原因：① 文件前两行是「游戏根: …」和语言包加载行，不是 JSON；② Windows PowerShell 的 `>` 默认写 **UTF-16LE**，不是 UTF-8。把这两点处理掉之后回灌就成功（实测）：

```text
[主题] 已导入: docs-roundtrip（往返实测主题）
```

实测导入的几条规矩：

| 情况 | 输出 | 退出码 |
|---|---|---|
| 正常导入 | `[主题] 已导入: docs-smoke（文档用临时主题）` | 0 |
| 缺 `id` 字段 | `[主题] 导入失败：缺少 id 字段` | 1 |
| id 非法（含 `/`、`..` 等） | `[主题] 非法主题 id: …（只允许字母、数字、点、下划线与短横线）` | 1 |
| 少一个颜色键 | `[主题] 导入失败：缺少必需颜色键 panel, fg, dim, border, accent, highlight, selection, ok, warn, error` | 1 |
| 多一个颜色键 | `[主题] 忽略未知颜色键: …`（其余照常导入） | 0 |
| 同名主题已存在 | `[主题] 已存在同名主题: docs-smoke（加 --force 覆盖）` | 1 |
| 同名 + `--force` | `[主题] 已导入: docs-smoke（文档用临时主题）` | 0 |
| JSON 语法错 / 空文件 | `[主题] 导入失败：JSON 解析错误 → …` / `[主题] 导入失败：内容为空` | 1 |
| 文件不存在 | `[错误] Could not find file '…'.`（英文是 .NET 原文） | 1 |

导入会写到 `Neutraled/themes/<id>.json`（目录不存在会自动建）。

### 5. 外部主题文件与 id 规则

- 外部主题放 `Neutraled/themes/<id>.json`，**一个文件一个主题**。
- 手工放进目录的主题**以文件名为准**（文件里的 `id` 字段不参与定址）；用 `--theme-import` 导入时才由文件里的 `id` 字段决定存成什么名字。
- 主题 id 只允许字母、数字、点、下划线、短横线（≤64 字符），不允许 `..`、`/`、`\`。非法名字的文件会被跳过并打一行「[主题] 非法主题 id: …」；坏 JSON 的文件打「[主题] 文件损坏，已跳过: …」，**不会让命令失败**。
- 外部主题与内置**同名就会覆盖内置**的那个（例如你自己写一个 `dark.json` 就能改掉默认深色）。

### 6. 切换主题：`--theme-use`

```powershell
& $exe --theme-use <主题id>
# [主题] 已切换到 <名称>
```

一次切换做三件事：

1. 把 `theme`（以及 `theme_font` / `theme_font_size`，字体名留空则写 null）写进 `Neutraled/config.json`；
2. 重新生成 `Neutraled/console-theme.json` —— 给**游戏内**控制台读的配色文件；
3. 当前进程内记下活动主题。

退出码：`0` 成功、`1` 找不到主题或写不进去、`2` id 非法。实测非法 id：

```powershell
& $exe --theme-use ../evil
# [主题] 非法主题 id: ../evil（只允许字母、数字、点、下划线与短横线）
# → 退出码 2，没有写任何文件

& $exe --theme-use docs-smoke-nosuch
# [主题] 找不到主题: docs-smoke-nosuch
# → 退出码 1
```

### 7. 游戏内怎么读主题：`console-theme.json`

游戏侧**不读** `Neutraled/themes/*.json`，只读 `Neutraled/console-theme.json`（由 `--theme-use` 生成）。所以换主题必须经过 builder（命令行或网页界面），手工往 `themes/` 丢文件不够。

文件结构（真实文件 660 字节，11 个颜色键齐全，下面只节选两个键）：

```json
{
  "schema": 1,
  "id": "dark",
  "name": "Тёмная (по умолчанию)",
  "colors": { "bg": "#101014", "panel": "#16161e", "…": "…" },
  "colors_bgr": { "bg": 1314832, "panel": 1971734, "…": "…" }
}
```

- `colors` 是给人（和网页界面）看的 `#RRGGBB`；
- `colors_bgr` 是给 GameMaker 用的 BGR 整数，换算公式是 `(b << 16) | (g << 8) | r`：以 dark 的 `bg = #101014` 为例，r=16、g=16、b=20 → `20*65536 + 16*256 + 16 = 1314832`。
- 为什么要有 BGR：GameMaker 的颜色常量本来就是 BGR 整数，预先算好，GML 侧就不用解析十六进制，也就没有解析失败的可能。
- 游戏侧读取顺序是 `program_directory` → `working_directory` 下的 `Neutraled/console-theme.json`，只认上面 11 个键名，其它键忽略。
- **读不到、缺 `colors_bgr`、JSON 坏了**，一律保持内置默认配色（日志里是 `[警告] console-theme.json 缺 colors_bgr，用默认配色` 或 `[错误] console-theme.json 解析失败，用默认配色`）。也就是说：没配过主题的机器，像素级和改造前一样。
- 成功时游戏日志会写一行 `主题已加载: <id>（11 个颜色）`。

## 二、语言

界面语言分两层，**它们不是一回事**：

| 层 | 谁在用 | 可选值 | 怎么切 |
|---|---|---|---|
| builder 自己的输出语言 | 命令行打印的每一行 | 只有 `zh` / `en` | `--lang zh\|en`、环境变量 `NTL_LANG`、`config.json` 的 `lang` |
| 界面语言（含游戏内） | 控制台、游戏内 UI | 6 种（见下） | `--lang-use <code>` 或游戏内 `lang <code>` |

### 1. 现在有哪几种语言

```powershell
& $exe --lang-list
```

```text
===== 语言包 =====
  * zh       中文                 内置
    en       English            内置
    de       Deutsch            外部（已加载）
    es       Español            外部（已加载）
    fr       Français           外部（已加载）
    ja       日本語                外部（已加载）
  当前语言: zh（--lang / NTL_LANG / config.json 的 lang 决定）
  共 6 种语言（内置 2，外部 4）
  外部语言包目录: E:\steam\steamapps\common\DELTARUNE\Neutraled\lang
```

| code | 名称 | 来源 | 覆盖率（实测） |
|---|---|---|---|
| `zh` | 中文 | 内置（编译进 builder 的表） | 1854/1854 = 100% |
| `en` | English | 内置 | 1854/1854 = 100% |
| `de` | Deutsch | 外部 `lang_de.json` | 1849/1849 + 游戏内 401/401 = 100% |
| `es` | Español | 外部 `lang_es.json` | 同上 |
| `fr` | Français | 外部 `lang_fr.json` | 同上 |
| `ja` | 日本語 | 外部 `lang_ja.json` | 同上 |

> 2026-09-26 起只随包 **6 种**：`ko` / `ru` / `zh-TW` 被移除。原因：这三种语言的正文（谚文 / 西里尔 / 繁体汉字）**游戏自身和汉化字库里都没有字形**，只能用系统 TTF 现渲染，风格与游戏像素字对不上；与其塞一个风格不一致的补充字库，不如不支持这些语言。语言包文件已移到 `E:\aiwork\out\Neutraled2\_feat\lang-removed\` 备份，想恢复只需拷回 `Neutraled/lang/`（并重新补字形）。

### 2. 语言包文件格式

`Neutraled/lang/lang_<code>.json`：

```json
{
  "schema": 1,
  "code": "ja",
  "name": "日本語",
  "map": {
    "保存": "保存",
    "操作": "操作",
    "透明度": "透明度"
  },
  "hint": {
    "保存": "Save"
  }
}
```

- **`map` 的 key 是中文原文**（就是 builder 里 `L("中文原文", …)` 的那个参数，也是 GML 表里中文那一侧的值），value 是译文。
- `code` 与文件名必须对得上：文件名是权威。实测包里的 `code` 字段和文件名不一致时会打一行「[语言] <文件> 里的 code 字段是 X，与文件名不一致（以文件名为准）」，然后照文件名用。
- `code` 只允许 ASCII 字母、数字、`-`、`_`，最长 24 字符，**不允许点号**（从根上挡掉 `..`）。
- `hint` 是导出模板时附的英文参考，游戏侧和覆盖率都不看它，纯给译者用。
- **空译文被丢弃**（留着会让界面显示空白）；**「译文 == 中文」的条目被保留** —— 日语/繁体和简体大量词条本来就同形（「警告」「注意」「操作」「位置」「透明度」…），丢掉会让覆盖率报出永远补不齐的假缺口（繁体包更是被大面积错杀）。真的没翻译，显示中文原文，和回退行为一致。
- 语言包坏了不会崩：读不了就跳过并打「[语言] 语言包损坏，已跳过: …」，其余语言照常。

### 3. 回退链（查不到的时候显示什么）

builder 侧（`L("中文原文")`）：

1. 当前语言是 `en` → 查内置英文表；查不到 → **原样返回中文原文**；
2. 当前语言是 `zh` → 直接返回中文原文；
3. 其它语言 → 查该语言的外部包；查不到 → **原样返回中文原文**。

游戏内（GML 侧）：非 `zh`/`en` 的语言包会被**合成**成一张新表 —— 拿内置 zh 表的每个「符号键 → 中文」，再去语言包的「中文 → 译文」里查一次：

- 查得到 → 用译文；查不到 → **保留中文**（宁可显示中文，也不能留空白）；
- 所以游戏内的键集永远和 zh 完全一致，语言包少几条不会让界面缺字；
- `zh` / `en` 不走这条路（它们是硬编码表，更准）；
- 加载成功会写一行 `语言包已加载: <code>（<命中数>/<总键数> 条命中译文）`。

**builder 界面语言的选择顺序**（同时决定这次命令行用什么语言打印）：

`--lang zh|en` → 环境变量 `NTL_LANG` → `config.json` 的 `lang` → `zh`。

**游戏内界面语言的选择顺序**（见 [THEMES-LANGS.md](THEMES-LANGS.md)）：`config.json` 的 `lang` → `global.lang` → `zh`；值是 `auto` 时按系统区域在 zh/en 之间选。

### 4. 命令

#### `--lang-list` —— 列出语言并直接附上覆盖率

`--lang-list` 的最后一段就是下面 `--lang-coverage` 的完整输出。

#### `--lang-coverage [语言码]` —— 覆盖率

```powershell
& $exe --lang-coverage
```

```text
===== 语言覆盖率 =====
  词条总数: 1854（当前语言 zh）
  游戏内词条: 416 条（api/ 的 zh 表）
[语言] zh  总键 1854  已译 1854  缺失 0  覆盖率 100.0%
[语言] en  总键 1854  已译 1854  缺失 0  覆盖率 100.0%
[语言] de  总键 1849  已译 1849  缺失 0  覆盖率 100.0%
[语言] de  游戏内 总键 401  已译 401  缺失 0  覆盖率 100.0%
…（es / fr / ja / ko / ru / zh-tw 同样各两行）
  未达 100% 的语言: 0 个
```

- **退出码 0 当且仅当所有语言都是 100%**，否则 1 —— 可以直接接进 CI。
- 给一个语言码时只统计那一种，例如 `--lang-coverage ja`；**单语言时不会打印「未达 100% 的语言」这一行**。
- 语言码非法（含 `/`、`..` 等）→ `[语言] 非法语言代码: …`，退出码 1。
- 语法合法但没这个包（例如 `--lang-coverage zz`）→ 照样统计，结果是 0%：`[语言] zz  总键 1849  已译 0  缺失 1849  覆盖率 0.0%`，退出码 1。

#### 「覆盖率」是怎么算的（重要口径）

分两栏，各自统计：

| 栏 | 分母 | 说明 |
|---|---|---|
| C# 侧（builder 与网页界面） | 1854（内置 zh/en）/ **1849**（外部语言） | 1849 = 含中文的键；那 5 个纯 ASCII 键（`Mod`、`ID`、`OK` 之类）不算 |
| 游戏内（api/ 的 zh 表） | 416（内置 zh/en）/ **401**（外部语言） | 401 = 含中文的键 |

**为什么外部语言只统计含中文的键**：语言包的 key 就是中文原文，纯 ASCII 的条目根本装不进语言包（它的 key 不是中文）。把它们算成「缺失」，会让每一种外部语言永远停在 99.x%，是个永远补不齐的假缺口。内置 `zh`/`en` 是编译期表，仍然按全集算。

另外：分母取自**编译期表**（不是「这次进程里加载过哪些包」），所以装不装语言包、装了几种，覆盖率数字都是可复现的。

#### `--lang-template <语言码> [--out 文件]` —— 导出待译模板

```powershell
& $exe --lang-template ja --out E:\tmp\template_ja.json
# [语言] 已导出待译模板: E:\tmp\template_ja.json（1854 条键）
# [语言] 模板已导出 → E:\tmp\template_ja.json
```

导出的是缩进 JSON，`map` 的每个 key 是中文原文、value 是空串，另附 `hint` 段放英文参考（没英文的留空，**不会把中文塞进 hint 骗译者**）。`--out` 省略时写到 `Neutraled/lang/template_<code>.json`。

不存在的语言码也能导（方便你从零做一种新语言），这时 `name` 是空串：

```powershell
& $exe --lang-template zz --out E:\tmp\template_zz.json
# [语言] 已导出待译模板: E:\tmp\template_zz.json（1854 条键）
# 文件头：{ "schema": 1, "code": "zz", "name": "", "map": { … } }
```

#### `--lang-use <语言码>` —— 切换界面语言

```powershell
& $exe --lang-use ja
# [语言] 已切换语言: ja
```

它把 `lang` 写进 `Neutraled/config.json`，之后（含游戏启动）都用这个语言。带 `--lang zh|en` 前缀只影响这次命令行的打印语言，不会改配置。

### 5. 从零加一种新语言（可复制流程）

```powershell
# 1) 导出模板（1854 条键）
& $exe --lang-template xx --out E:\tmp\lang_xx.json
# 2) 翻译文件里 map 的 value（key 一个字都别改），顺手把 name 改成母语写法
# 3) 存成 Neutraled\lang\lang_xx.json
# 4) 确认被认出来（会显示「外部（未加载）」或「外部（已加载）」）
& $exe --lang-list
# 5) 看还差多少（退出码 0 = 全部 100%）
& $exe --lang-coverage xx
```

- 文件名决定 code：`lang_xx.json` → `xx`；大小写以文件名为准（`lang_ja.json` → 列表里的 `ja`）。
- 翻译时**留空就是回退中文**，所以可以分批交给不同的人，先上不完整的也能用。
- 想让所有人都用上，最后一步是 `--lang-use xx`（会写 `config.json`）。
- 游戏内也一样：只要 `Neutraled/lang/lang_xx.json` 在，游戏启动或运行 `lang xx` 时会自动加载。

### ⚠️ 踩坑记录

- `--theme-show <不存在的 id>` **返回 0**（只有一行「找不到主题」），脚本里要看输出而不是退出码；`--theme-use` 找不到才是 1。
- 命令行下 `--gb-install` / `--queue-run` 不会自动装 mod（见 [GB.md](GB.md)），但主题与语言不受影响。
- **主题名会按生成 `console-theme.json` 那一刻的界面语言写死**：本机这个文件里的 `name` 现在是俄语 `Тёмная (по умолчанию)`，因为它是先前 `lang=ru` 时生成的。之后切回中文不会自动重写它 —— 想刷新就再跑一次 `--theme-use`。
- 手改 `console-theme.json` 时**红蓝容易写反**：GameMaker 的颜色是 BGR，不是 CSS 的 RGB。写反的表现是颜色整体偏蓝/偏红。别自己算，改 `themes/*.json` 里的 `#RRGGBB` 再 `--theme-use` 一次，让 builder 帮你算 `colors_bgr`。
- `console-theme.json` **缺 `colors_bgr` 就是整套默认配色**（不是只用缺的那几个键补默认），所以别只写 `colors`。
- 语言码里别用点号（会被判非法）；语言码大小写以文件名为准。
- 语言包里的空译文会被丢弃，**但译文与中文完全相同是合法的**（同形词大量存在），不要为了「让覆盖率好看」把同形词删掉。
- 主题/语言包都是**外部数据文件**，删掉 `themes/` 或 `lang/` 里的东西只会退回内置，不会弄坏安装。
- **builder 的打印语言跟随 `config.json` 的 `lang`**：设成 `ru` 时，连「游戏根」「已加载 N 个外部语言包」「===== 主题 =====」都会变成俄语（实测）。`--lang zh|en` 只影响这一次调用。

### 已知限制

- **`--theme-use` 与 `--lang-use` 的成功路径本次没有实跑**（它们会写 `Neutraled/config.json`，本次任务禁止改这个文件）。上面关于「写 config.json / 重新生成 console-theme.json / 进程内记录活动主题」的描述来自源码，不是实测；已实跑的是它们的失败路径（非法 id → 2、找不到 → 1）。
- **未在游戏里验证换主题后的画面**：只确认到 `console-theme.json` 被正确写出、以及游戏侧读取逻辑（读不到就退回默认配色）。配色在画面上的最终效果没有截图证据。
- **游戏侧与 builder 侧对「译文 == 中文」的处理不一致**：GML 侧会把这类条目丢弃（回退中文），builder 侧保留（所以覆盖率算它已译）。两者显示出来的字**完全一样**（值就等于原文），差别只在覆盖率统计口径。以 [MANAGE.md](MANAGE.md) §9 那句「译文==中文的占位一律丢弃」为准时，它描述的其实是游戏侧行为。
- **覆盖率只覆盖两处文案表**：builder 的编译期表（1854 条）与 `api/ntl_i18n_init.gml` + `api/ntl_i18n_out.gml` 的 zh 表（416 条）。游戏里其它硬编码文本、mod 自己打印的文本、以及注入产物里的注释都不在统计范围内（见 [THEMES-LANGS.md](THEMES-LANGS.md) 的「有意保持中文」清单）。
- **语言包没有版本号**：换了一版 builder、新增了词条，旧语言包不会提醒你「有新词条待译」，只能自己重跑 `--lang-coverage` 对比。
- **`--lang-list` 的「外部（已加载）」是本次进程的状态**，不代表磁盘上没有别的包；判断有没有某个语言，看它有没有在这一行里出现。
- 主题只影响**颜色**：字体名与字号虽然会被写进 `config.json`（`theme_font` / `theme_font_size`），但本次没有验证它们在游戏内的实际效果。
- 未在 macOS / Linux 上验证主题与语言包路径；本文实测都在 Windows 上。

---

## Neutraled 中英双语

> 覆盖 **Neutraled 自己产生的用户可见文本**：游戏内控制台、命令说明、HUD、章节选择器、错误提示、
> builder CLI 输出、GUI / Studio 界面、Kristal 版控制台、控制台导出文件。
> **游戏原版的文字完全不动**（章节名、对话、菜单、存档界面保持游戏自己的语言）。

---

### 语言来源（优先级）

**游戏内**（GML 侧，`api/ntl_i18n_init.gml`）：

1. `config.json` 的 `"lang"` 字段：`"zh"` / `"en"` / `"auto"`
2. `auto` → 跟随游戏语言（读 `global.lang`）
3. 缺省 → `zh`

**builder CLI**（C# 侧，`builder/Lang.cs`）：

1. 命令行 `--lang zh|en`
2. 环境变量 `NTL_LANG`
3. `config.json` 的 `"lang"`
4. 缺省 `zh`

```json
{ "lang": "auto" }
```

```bat
:: 只让本次命令输出英文
ntl-builder --lang en --info --chapter chapter4
set NTL_LANG=en
```

### 游戏内切换

控制台里（F2）：

```
> lang           查看当前语言
> lang zh        切换中文
> lang en        切换英文
> lang auto      跟随游戏语言
```

切换**立即生效**（控制台标题、命令说明、HUD 立刻变）。另外 `lang get <键>` / `lang set <键> <文本>`
可临时覆盖单条文案（写入 `global.ntl_lang_overrides`）。

---

### 覆盖范围

| 类别 | 内容 |
|---|---|
| **控制台** | 标题、关闭提示、未识别命令提示、帮助文字、`console save` 导出文件的表头 |
| **命令** | 全部命令的**说明**与**用法**（`cmd.*.d` / `cmd.*.u`） |
| **分类** | 信息 / 状态 / 操作 / 调试 / 自动化 / mod 命令 |
| **HUD** | 移动提示、交互提示、控制台提示、附近对象、已交互 |
| **章节选择器** | 标题、页码、搜索提示、`[改]`/`[线]`/`[外]` 标记、`[无内容]`、底部按键说明、窗口标题 |
| **操作反馈** | 跳转章节、加载地图、创建/销毁实例、设置变量、截图；speed / warp / hp / god / pause / step / dump / watch / crash / freeze |
| **错误提示** | 脚本缺失、语法错误、运行错误、异常隔离，以及 Lua 报错的中文解释 + 建议 |
| **mod 相关** | 注册命令、导出信息、依赖满足/缺失 |
| **builder CLI** | `--help`、各命令的进度/结果/错误输出（异常消息也走文案表） |
| **GUI / Studio** | 按钮、标签、对话框、状态栏（`gui/Localizer.cs`、`studio/Localizer.cs`） |
| **Kristal 控制台** | `kristal/ntlconsole/lib.lua` 的 38 条文案 + 命令说明 |

---

### 不覆盖的部分（有意保持中文）

| 位置 | 原因 |
|---|---|
| `ntl_log(...)` → `dr-api.log` | 开发向日志；三套件（`_test/fulltest-*.ps1`）与排障依赖其中的中文标记 |
| 注入进 `data.win` 的产物代码与注释 | 机器产物，不是界面文字 |
| 生成的报告 / 文档 | `LAYER_REPORT.txt`、`CONVERSION.txt`、`KRISTAL-MERGE.txt`、Kristal 转换/验证报告、API 文档、`new-chapter` 脚手架模板、`mod.json` 的 description |
| `ContentCheck.Report.Verdict` | 内部数据哨兵（`"通过"/"警告"/"跳过"/"失败"`），被 `report.json` 与 `DeploySelfCheck.cs:191` 按值比较；只在打印点 `ContentCheck.cs:232` 映射成英文 |
| 用户自己的 `mods/*` | 用户内容，项目不改写 |
| `_test/*.ps1` | Windows PowerShell 5.1 把无 BOM 的 UTF-8 当 ANSI 读，脚本必须 ASCII-only |

---

### 文案表（**419 条**）

| 文件 | 内容 |
|---|---|
| `api/ntl_i18n_init.gml` | 基础表：zh/en 各 170 条（含控制台/HUD/分类/命令说明/错误提示/章节选择器） |
| `api/ntl_i18n_out.gml` | 第十一批补全表：zh/en 各 252 条，**后续新增文案只往这里加** |

两表键集完全一致（脚本校验：only-zh / only-en / 空值 / 占位符不一致 均为 0）。

追加位置：`api/ntl_i18n_out.gml` 末尾锚点行**之前**

```gml
// @@NTL_I18N_OUT_APPEND@@   ← 在这一行之前插：
// ds_map_add(_zh, "键", "中文");
// ds_map_add(_en, "键", "English");
```

| key | 中文 | English |
|---|---|---|
| `console.title` | Neutraled 控制台 | Neutraled Console |
| `console.close` | [F2 关闭] | [F2 close] |
| `cat.info` | 信息 | Info |
| `hud.move` | 方向键/WASD 移动 | Arrows/WASD to move |
| `root.title` | Neutraled — 章节选择 | Neutraled — Chapter Select |
| `root.page` | 第 {1} / {2} 页 | page {1} / {2} |
| `root.keys` | ↑↓ 选择    ←→ 翻页    Enter/Z 进入    F 搜索    Backspace 删字 | ↑↓ select    ←→ page    Enter/Z enter    F search    Backspace delete |
| `out.fail` | [失败] | [Failed] |
| `save.head` | ===== Neutraled 控制台导出 ===== | ===== Neutraled console export ===== |
| `adv.prefix` |         建议: {1} |         advice: {1} |

**键命名前缀**：`console.*` `cat.*` `msg.*` `hud.*` `err.*` `adv.*` `cmd.*` `root.*` `save.*` `hook.*`（基础表）；
`out.*`（通用结果：fail / err / warn / tip / note / dev / alias / override / modcmd / script）、
`speed.*` `warp.*` `hp.*` `god.*` `pause.*` `resume.*` `step.*` `dump.*` `watch.*` `crash.*` `freeze.*` `st.*` `act.*` `draw.*` `cap.*`（第十一批）。

---

### 硬性约定

1. **中文文案与改造前的硬编码中文逐字一致**（三套件与真机断言都在匹配这些中文），英文是新写的。
2. 消息写成**单条模板**而不是片段拼接：`ntl_console_log(ntl_ts("speed.cur", [string(_cur), string(_orig)]));`
   （zh `"当前 FPS: {1}（原始 {2}）"` / en `"Current FPS: {1} (original {2})"`）。
3. 控制台输出的**着色/过滤前缀**必须中英都认：`ntl_console_line_pass.gml:7-11`、
   `ntl_console_draw.gml:77/79` 同时匹配 `[错误]/[Error]/[失败]/[Failed]/[警告]/[Warning]/[注意]/[Note]`。

---

### Kristal 版控制台（`kristal/ntlconsole/lib.lua`）

- 语言由文件里的占位符 `local NTL_LANG = "@@NTL_LANG@@"` 决定；
  `builder/FuseZip.cs` 的 `ConsoleLuaBytes()` 在注入时把它替换成当前语言（`zh`/`en`）。
- 注入到三处：exe 同目录 `ntl-console.lua`（热更新入口）、`src/ntlconsole.lua`、`<mod>/libraries/ntlconsole/lib.lua`。

---

### 实现说明

| 文件 | 作用 |
|---|---|
| `api/ntl_i18n_init.gml` | 基础文案表 + 语言探测（`config.json.lang` → `global.lang` → zh） |
| `api/ntl_i18n_out.gml` | 第十一批补全表；唯一追加点 `// @@NTL_I18N_OUT_APPEND@@` |
| `api/ntl_t.gml` | `ntl_t(key)` 取文案（找不到返回键名本身） |
| `api/ntl_ts.gml` | `ntl_ts(key, [v1, v2, …])` 取文案并按下标替换 `{1}{2}…`（个数不限） |
| `api/ntl_tf.gml` | `ntl_tf(key, v1, v2, v3)` 旧接口，最多 4 个占位符 |
| `api/ntl_lang_set.gml` | `lang` 命令（zh/en/auto + `lang set/get` 覆盖） |
| `api/ntl_get_lang_string.gml` / `ntl_set_lang_string.gml` / `ntl_apply_lang_overrides.gml` | 单条文案覆盖 |
| `api/ntl_console_line_pass.gml` / `ntl_console_draw.gml` | 中英双前缀的过滤与着色 |
| `builder/Lang.cs` + `builder/LangTable_{Program,Deploy,Mods,Tools,Extra,Features}.cs` | builder CLI 双语（**去重 1975 条**：Program 274 / Deploy 250 / Mods 290 / Tools 214 / Extra 452 / Features 504；`L("中文原文", args…)` 以中文为 key 查英文表，未命中原样返回中文） |
| `builder/LintRules3.cs` | `--lint` 的文案防回归规则（见下节） |
| `gui/Localizer.cs`（204 条）、`studio/Localizer.cs`（43 条） | GUI / Studio 界面（`T("中文原文")`） |
| `kristal/ntlconsole/lib.lua` | Kristal 控制台（38 条 + `@@NTL_LANG@@` 占位符） |

---

### 防回归（`ntl-builder --lint`）

`builder/LintRules3.cs` 的三条规则：

| 规则 | 检查 |
|---|---|
| 13 文案硬编码 | 用户可见的控制台输出中出现中文字面量（`ntl_console_log/write/warn/err`）→ 必须走文案表 |
| 14 词条缺失 | `ntl_t/ntl_ts/ntl_tf("键")` 引用了表里不存在的键（运行时会直接显示键名） |
| 15 文案空值 / 文案缺一侧 / 占位符不一致 | 表自身：译文为空、只有单一语言、`{n}` 占位符两侧不一致 |

- 故意中英并排的语句（如 `"Language: " + _l + "  |  语言: " + _l`）在行尾加标记 `// ntl:i18n-exempt`。
- `ntl_log(...)` 开发日志、注入产物、生成文档**不在**规则 13 的范围内（有意保留中文）。

**builder 英文表是生成物（改表只能改源 JSON）**：`builder/LangTable_*.cs` 由
`E:\aiwork\out\Neutraled2\_i18n-builder\gen-tables.mjs` 整文件重写（源 = 同目录 `keys-*.json`：Program/Deploy/Mods/Tools 各一份、
Extra = `keys-extra-g1..g6.json`（g5 = 2026-10 游戏更新检测 105 条，g6 = 回填历史手补键）、Features = `keys-feat-*.json` 七份）。

- 用法：在该目录 `node gen-tables.mjs --check`（只校验）→ `node gen-tables.mjs`（写盘）；脚本自检空译文、`{0}` 占位符一致性、
  以及**跨组同 key 译文不同**（`[DUP-DIFF]` ⇒ 拒绝写盘）。所以要给同一句中文写两种英文，必须**先拆成两个不同的中文 key**
  （例：DeploySelfCheck 的 `已扫描 ` 与 KristalConvert 的 `扫描 `）。
- **重跑前先确认源 JSON 是全量**：历史上有人用 `_patch-tables.mjs` 直接往 .cs 补键而没回填 JSON，重跑会把这些键**静默删掉**
  （2026-10 实测 7 条：`builder/Program.cs` 的「[重做] …输入未变…」+ `builder/DeploySelfCheck.cs` 的 6 条扫描文案；已分别回填
  `keys-program.json` 与 `keys-extra-g6.json`）。**零丢键校验**：重跑后把 `git show HEAD:builder/LangTable_*.cs` 的
  `d["key"]` 集合与新文件比对，必须「丢失 0 / 同 key 译文改变 0」。
- 语言来源（`builder/Lang.cs:83 Init`）：`--lang` > `NTL_LANG` > `<游戏根>/Neutraled/config.json` 的 `lang` > **按 `CultureInfo.CurrentUICulture` 猜**
  （zh ⇒ zh，否则 en）。**非中文系统 + 无 config.json 会直接出英文** ⇒ 断言中文文案的测试脚本要显式加 `--lang zh`。
- 辅助校验脚本（开发工作区，非产品）：`E:\aiwork\out\Neutraled2\_i18n-gml-check.mjs`（GML 表键集/占位符/引用完整性）、
  `E:\aiwork\out\Neutraled2\_i18n-builder\_check-wrap.mjs`（builder 的 `L()` 包裹完整性）、
  `_check-gui-keys.mjs`（GUI/Studio 词条）、`_scan-gml-left2.mjs`（GML 残留中文清单）。
