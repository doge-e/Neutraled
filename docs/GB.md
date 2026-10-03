# GameBanana：搜索、一键安装与下载队列

> 一句话：这一篇解决「不开浏览器也能从 GameBanana 找 DELTARUNE 的 mod、下载下来、排进队列，并把不想要的东西挡在搜索结果外面」。
>
> 本文是 [MANAGE.md](MANAGE.md) §5/§6 的深挖版：只讲 GameBanana、下载队列、黑名单这三块；下载完之后怎么导入、导入的格式转换规则看 [PIPELINE.md](PIPELINE.md)；部署与缓存看 [PIPELINE.md](PIPELINE.md)。

## 目的

GameBanana 相关的功能在 builder 里是四个开关族加一个黑名单：

| 开关族 | 干什么 |
|---|---|
| `--gb-search` / `--gb-files` / `--gb-install` | 搜索、看文件表、下载并导入 |
| `--queue-add` / `--queue-list` / `--queue-run` / `--queue-remove` | 下载队列（先排队、之后一次性下载） |
| `--block-list` / `--block-add` / `--block-remove` | 黑名单（只过滤搜索结果列表） |
| `--fetch-mod <gamebananaId> [--model Mod\|Wip]` | 更早的入口：下载 + 自动识别格式转换，见 [PIPELINE.md](PIPELINE.md) |

GameBanana 上 **DELTARUNE 的 game id 是 6755**（源码里写作 `GameBanana.DeltaruneGameId = 6755`），所有搜索都带 `_idGameRow=6755`，所以不会搜到别的游戏。注意 Kristal 是**另一个游戏条目（id 16251）**，不在 6755 里，见 [PIPELINE.md](PIPELINE.md)。

本文所有命令行示例都真跑过（2026-09-26，本机 Windows 10.0.19045 + .NET 9.0.20），输出原样粘贴。命令里的 `$exe` 指：

```powershell
$exe = "E:\steam\steamapps\common\DELTARUNE\Neutraled\builder\bin\Release\net9.0\ntl-builder.exe"
```

**每条 GameBanana / 队列 / 主题 / 语言命令都会先打印两行公共前缀**，后面不再重复贴。抓这些输出时 `Neutraled/config.json` 的 `lang` 是 `zh`，所以前缀是中文；这个字段也决定 builder 自己的打印语言（设成 `ru` 会连 builder 的输出一起变俄语，见 [THEMES-LANGS.md](THEMES-LANGS.md)）：

```text
游戏根: E:\steam\steamapps\common\DELTARUNE
[语言] 已加载 7 个外部语言包: de, es, fr, ja, ko, ru, zh-tw（共 15715 条译文）
```

## 1. 搜索：`--gb-search`

```powershell
& $exe --gb-search kris
```

```text
[GB] 搜索「kris」（每页 15 条）
[GB] 序号 | id | 名称 | 作者 | 下载量 | 更新时间
[GB] 1 | 705025 | Kris over Kris THE Rory Jr. demo | Pavliggg | 0 | 
[GB] 2 | 452441 | NES Mario Over Kris Mod | TopTextBottomText | 0 | 
[GB] 3 | 601887 | Deltarune Chara Mod | des413 | 0 | 
…
[GB] 13 | 250818 | Frisk | DazzlingNikki | 0 | 
[GB] 共 13 条结果
```

- 关键词可以是多个词，builder 用空格拼起来一起提交：`--gb-search kris over kris`。
- `--per-page N` 只是**透传**给 API 的参数，**返回条数由 GameBanana 决定**。实测 `--gb-search "kris over kris" --per-page 3` 的表头写「每页 3 条」，但照样返回 13 条。
- 没有结果时打一行「[GB] 没有搜索结果」；搜索失败时打「[GB] 搜索失败: …」并返回 1。
- 表格里的 **下载量列恒为 0、更新时间列为空**：这个搜索端点的返回里没有这两个字段的值，不是你的网络问题。

## 2. 文件表：`--gb-files`

```powershell
& $exe --gb-files 705025
```

```text
[GB] mod 705025（类型 Mod）共 2 个文件
[GB] 序号 | 文件 id | 文件名 | 体积 | 说明
[GB] 1 | 1786099 | krisoverkrisc1.zip | 519 KB | THE ultimate
[GB] 2 | 1815820 | rorydemo.zip | 810 KB | demo with no Kris
```

`--gb-type` 默认 `Mod`，用来指定条目类型（`Mod` / `Wip` / …）。它**不在 `--help` 里列出来**，但被实现接受：

```powershell
& $exe --gb-files 705025 --gb-type Wip
# → 退出码 1
# [GB] 取文件表失败: Response status code does not indicate success: 404 (Not Found).

& $exe --gb-files 705025 --gb-type NoSuchType
# → 退出码 1
# [GB] 取文件表失败: 'W' is an invalid start of a value. LineNumber: 1 | BytePositionInLine: 0.
```

第二行看着莫名其妙，其实是 API 对未知类型返回了**非 JSON 文本**，解析器报的是那句话。类型名只允许字母、最长 16 位（见 §3）。

## 3. 一键安装：`--gb-install`

```powershell
& $exe --gb-install <modId> [--file N] [--chapter 章节] [--force]
```

流程是：取文件表 → 选中一个文件（`--file` 指定，否则取第一个）→ 下载到 `Neutraled/dl/` → 交给导入器导入。实测错误路径：

```powershell
& $exe --gb-install 705025 --file 999999
# → 退出码 3
# [GB] 一键安装：mod 705025（类型 Mod）
# [GB] mod 705025 的文件表里没有文件 id 999999
# [GB] 可选的文件 id：1786099, 1815820

& $exe --gb-install 705025 --file 0
# → 退出码 3
# [GB] 文件 id 非法：0

& $exe --gb-install 705025 --gb-type ../etc
# → 退出码 3
# [GB] 类型名非法：../etc（只允许字母，最长 16 位）

& $exe --gb-install 999999999
# → 退出码 1
# [GB] 安装失败: Response status code does not indicate success: 404 (Not Found).
```

真实下载（这条真跑过，网络下载完成）：

```powershell
& $exe --gb-install 705025 --file 1786099
```

```text
[GB] 一键安装：mod 705025（类型 Mod）
[GB] 选中文件：krisoverkrisc1.zip（id 1786099，519 KB）
[下载] 开始下载：705025-1786099-krisoverkrisc1.zip
[下载] 17% (92/519 KB)
[下载] 79% (412/519 KB)
    curl 第 1 次: 519 KB / 519 KB
[下载] 100% (519/519 KB)
[下载] 完成：705025-1786099-krisoverkrisc1.zip（519 KB）
[GB] 未注入导入器（ImportHook），文件已下载但未安装：E:\steam\steamapps\common\DELTARUNE\Neutraled\dl\705025-1786099-krisoverkrisc1.zip
[GB] 请让 Program.cs 启动时给 GbBrowse.ImportHook 赋值，或手工导入上面的文件
```

**退出码是 2，不是 0** —— 命令行上的 `--gb-install` 只下载、不自动导入。原因是导入钩子只在网页界面那条路上注入：

| 入口 | 会不会自动导入 |
|---|---|
| `--gb-install` / `--queue-run`（命令行） | **不会**，下载完成后停在「已下载未安装」，退出码 2 |
| `--web` 网页界面里的 GameBanana 面板 | 会，面板启动时注入了导入器 |

这一点和 [MANAGE.md](MANAGE.md) §6 里「会调用与 `--import-mod` 相同的导入器」的说法**只在网页界面成立**。命令行下请把打印出来的路径交给 `--import-mod`，或者直接用 `--web`。

### 退出码

| 码 | 含义 |
|---|---|
| 0 | 成功（含批量安装里至少成功、且没有失败） |
| 1 | 网络或 IO 失败（含 404）、导入失败时**透传导入器自己的返回码** |
| 2 | 文件已下载，但没注入导入器，无法自动安装 |
| 3 | 参数非法（文件 id ≤ 0、类型名非法、指定的文件不存在） |

## 4. 黑名单：`--block-*`

黑名单存在 `Neutraled/blacklist.json`（实测初始内容就是一行空表）：

```json
{"entries": []}
```

`kind` 有三种，匹配方式不同：

| kind | 匹配对象 | 匹配方式 |
|---|---|---|
| `id` | 条目的 mod id | **精确**相等 |
| `name` | 条目名称 | 大小写不敏感**子串** |
| `category` | 条目类型（Mod / Wip / …） | 大小写不敏感**子串** |

```powershell
& $exe --block-add 999001 --kind id --note docs smoke test
# [黑名单] 已加入 999001

& $exe --block-list
# [黑名单] 文件：E:\steam\steamapps\common\DELTARUNE\Neutraled\blacklist.json
# [黑名单] 共 1 条
# [黑名单] kind=id  value=999001  note=docs smoke test  added=2026-09-26 12:32:56
```

**过滤效果实测**：把 `Kris over Kris` 按名字加进黑名单后再搜同一个词，13 条变 12 条，并多出一行过滤计数：

```text
[GB] 黑名单已过滤 1 条
[GB] 2 | 452441 | NES Mario Over Kris Mod | TopTextBottomText | 0 | 
```

再把类型 `Mod` 整个拉黑，同一个搜索就空了：

```text
[GB] 黑名单已过滤 13 条
[GB] 没有搜索结果
```

**删除必须带同一个 kind**，否则删不掉（实测）：

```powershell
& $exe --block-remove "Kris over Kris"
# → 退出码 1
# [黑名单] 没找到 Kris over Kris

& $exe --block-remove "Kris over Kris" --kind name
# → 退出码 0
# [黑名单] 已移除 Kris over Kris

& $exe --block-remove Mod --kind bogus
# → 退出码 1
# [黑名单] 未知 kind：bogus（可用 id/name/category）
```

两个实测踩坑：

- **重复添加照样返回 0**：先打一行「[黑名单] 已存在 999001」，紧接着还是打「[黑名单] 已加入 999001」，退出码 0。要判断有没有真加进去，看第一行。
- **黑名单只过滤搜索列表**。源码里过滤只发生在两处：`--gb-search` 的结果、网页界面的搜索结果。直接给 mod id 的 `--gb-files`、`--gb-install`、`--fetch-mod` **不受黑名单影响** —— 黑名单是「别让我再看到它」，不是「禁止安装它」。
- `blacklist.json` 坏了不会让程序崩：读不了就按空表处理，并打一行「[黑名单] 文件损坏，已忽略: …」。这个文件也接受尾逗号与 `//` 注释，手改不用太小心。

## 5. 下载队列：`--queue-*`

队列让你**先排队、之后再一次性下载**，适合一次挑好几个 mod。队列文件是 `Neutraled/dl/queue.json`，下载物放在 `Neutraled/dl/`。

```powershell
& $exe --queue-add 705025 --name "Kris over Kris" --file 1786099
# [队列] 已入队：gb-705025-1786099-20260926123621（mod 705025，Kris over Kris）
# [队列] 已入队 gb-705025-1786099-20260926123621

& $exe --queue-list
# [队列] 队列文件：E:\steam\steamapps\common\DELTARUNE\Neutraled\dl\queue.json
# [队列] 序号 | id | mod | 名称 | 状态 | 文件 | 体积 | 已下载 | 说明
# [队列] 1 | gb-705025-1786099-20260926123621 | 705025 | Kris over Kris | queued | krisoverkrisc1.zip | 519 KB | 0 KB | 
# [队列] 共 1 项
```

- 条目 id 形态是 `gb-<modId>-<fileId 或 0>-<yyyyMMddHHmmss>`；不给 `--file` 时 fileId 位是 `0`。
- 不给 `--name` 时名字默认是 `GameBanana #<modId>`，同时会**同步问一次文件表**把 fileId/url/体积解析出来 —— 所以入队时也建议联网，否则队列里只有一个 mod id。
- 同一个 mod 同一个文件重复入队不会加第二条，会打「[队列] 已存在相同的条目，直接复用：…」。
- 入队成功后命令行会额外打一行「[队列] 已入队 <id>」（方便脚本取 id）。

### 执行：`--queue-run`

```powershell
& $exe --queue-run
# → 退出码 1
# [队列] 开始执行：1 项
# [队列] 处理 gb-705025-1786099-20260926123621：mod 705025（Kris over Kris）
# [队列] 开始下载：gb-705025-1786099-20260926123621 → 705025-1786099-krisoverkrisc1.zip
# [队列] 下载完成：gb-705025-1786099-20260926123621（519 KB）
# [队列] 失败：gb-705025-1786099-20260926123621 → 未注入导入器（ImportHook），无法自动安装
# [队列] 执行结束：成功 0 / 失败 1 / 跳过 0
```

`--queue-run` 的返回码是**失败条数 > 0 就返回 1**，所以脚本里可以直接用退出码判断。只下载不安装（命令行推荐的用法）：

```powershell
& $exe --queue-run --no-install
# → 退出码 0
# [队列] 跳过下载（本地已有文件）：gb-705025-1786099-20260926123621
# [队列] 已就绪，等待安装（未开启自动安装）：gb-705025-1786099-20260926123621
# [队列] 执行结束：成功 1 / 失败 0 / 跳过 0
```

- 空队列直接打「[队列] 队列为空」返回 0（不会创建 `dl` 目录）。
- `--delete-after` 只在**安装成功**后才删文件；失败时保留（实测失败后文件仍在）。
- 每处理完一项就立刻写盘，所以**中断后可以直接重跑**，已经下好的项会被跳过。
- `--queue-remove <条目id>` 移除条目；`--delete-file` 同时删掉已下载的文件（只允许删 `dl` 目录里的东西）。条目 id 不存在时返回 **3**。

### 状态机

条目状态在 `queue.json` 里是 ASCII 原值，方便脚本判断：

`queued` → `downloading` → `downloaded` → `ready-to-install` → `installed`，另有 `failed` 与 `cancelled`。

实测这条链在命令行下会停在 `ready-to-install` 并把原因写进错误列（就是上面那句「未注入导入器」）。

### queue.json 结构

根对象是 `{"items":[…]}`（也兼容「根直接是数组」的手写文件）。每个条目的字段名是 camelCase：

| 字段 | 说明 |
|---|---|
| `id` | `gb-<modId>-<fileId>-<时间戳>` |
| `modId` / `modName` / `fileId` / `fileName` | 条目与文件信息 |
| `url` | 下载直链，实测是 `https://gamebanana.com/dl/1786099` |
| `state` / `error` | 状态与失败原因 |
| `size` / `got` | 期望字节数与已下载字节数（实测 532001） |
| `localPath` | 本地文件路径 |
| `kind` | 默认 `Mod` |
| `added` / `updated` | `2026-09-26T12:33:04` 形式 |

队列文件坏掉不会崩：坏 JSON 打「[队列] 队列文件损坏，按空队列处理: …（原因）」，id 非法/重复/mod id 非法的条目会被逐条忽略并说明原因。

## 6. 下载：curl、缓存与断点续传

下载只有一个实现（队列刻意复用搜索那套，不养第二份），顺序固定：

1. **已经有这个文件且体积相符 → 直接跳过**（幂等）：
   `[下载] 已存在且体积相符，跳过下载：705025-1786099-krisoverkrisc1.zip（519 KB）`
2. **Windows 上先用系统 curl**（本机 `C:\WINDOWS\system32\curl.exe`，`--platform-info` 能看到）。参数是
   `curl.exe -s -L -m 900 -C - -A "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" -e "https://gamebanana.com/" -o <文件> <url>`，
   其中 **`-C -` 就是断点续传**，最多重试 4 次，两次之间等 1.5 秒。
3. curl 不可用或失败，回退到内置 HTTP 下载（每 10% 打一行进度；**不支持续传**）。

**断点续传实测**：把一个下好的 532001 字节文件截断到 200000 字节，再跑同一条命令：

```text
[下载] 开始下载：705025-1786099-krisoverkrisc1.zip
[下载] 37% (195/519 KB)
[下载] 55% (287/519 KB)
[下载] 100% (519/519 KB)
[下载] 完成：705025-1786099-krisoverkrisc1.zip（519 KB）
```

体积恢复成 532001 字节，只补了缺的那一段 —— curl 续传确实在工作。

体积校验的规则：

- 期望体积 `<= 0` 时**不做校验**（GameBanana 偶尔给 0），下完就算成功；
- 期望体积 > 0 时，curl 下完体积不符就打「[下载] 体积不符：期望 X KB，实际 Y KB」并按失败处理，内置下载回退同样校验；
- 0 字节一律算失败；
- 界面上显示的是整数 KB（`字节数 / 1024` 取整），所以「519 KB」对应的是 532001 字节。

下载文件名形如 `Neutraled/dl/<modId>-<fileId>-<原文件名>`，原文件名会先砍掉目录、只保留安全字符（防止 GameBanana 的文件名里带 `../` 写到别处去）。

## 7. GameBanana 接口与 URL 形态

builder 只碰三个接口，都用 `https://gamebanana.com/` 作 Referrer、UA 是 `Neutraled/0.1 (mod manager)`：

| 用途 | URL |
|---|---|
| 搜索 | `https://gamebanana.com/apiv11/Util/Search/Results?_sModelName=Mod&_sOrder=best_match&_sSearchString=<关键词>&_nPerpage=<N>&_idGameRow=6755` |
| 文件表 | `https://gamebanana.com/apiv11/<类型>/<modId>/ProfilePage` |
| 下载 | 文件表里给的直链，实测落在 `https://gamebanana.com/dl/<fileId>` |

GameBanana 的**网页**地址（给人看、给浏览器打开用，程序不用）：

| 形态 | 实测 |
|---|---|
| `https://gamebanana.com/mods/<modId>` | HTTP 200 |
| `https://gamebanana.com/wips/<modId>` | HTTP 200 |
| `https://gamebanana.com/dl/<fileId>` | HTTP 200，返回的字节数与下载下来的文件**逐字节一致** |

game id 过滤是真的生效（实测把 `_idGameRow` 从 6755 改成 9999，返回体从 52 KB 变成 132 字节的空结果集）。

## 8. 数据布局

| 路径 | 内容 |
|---|---|
| `Neutraled/dl/queue.json` | 下载队列 |
| `Neutraled/dl/<modId>-<fileId>-<文件名>` | 下载下来的文件 |
| `Neutraled/blacklist.json` | 黑名单 |

这三样都是数据文件，删掉只会丢队列/黑名单，不会影响已安装的 mod。

## ⚠️ 踩坑记录

- **搜索结果的「下载量」永远是 0、「更新时间」永远是空**：端点没返回这两个值。
- **`--per-page` 不生效**：它只是透传参数，返回条数由 GameBanana 决定。
- **命令行的 `--gb-install` / `--queue-run` 不会自动安装**，退出码 2 / 1；要自动安装就用 `--web` 面板，或者自己接 `--import-mod`。
- **`--block-remove` 必须带与添加时相同的 `--kind`**，否则「没找到」并返回 1。
- **重复 `--block-add` 仍返回 0**，别只看退出码。
- **黑名单管不到按 id 直接下载**：它只过滤搜索列表。
- **断点续传只有 Windows + 系统 curl 这条路**；回退到内置下载时是整文件重下。
- `--gb-type` 拼错不会报「类型非法」，而是从服务器拿回 404 或一段非 JSON 文本，报错很难懂。

## 已知限制

- **未实测「下载后真正装进游戏」**：本文只覆盖命令行路径，它必定停在「已下载未安装」（退出码 2）。把它装进游戏需要网页界面或手工导入，尚未在游戏里验证过。
- **未实测非 Windows 平台**：curl 分支被 `OperatingSystem.IsWindows()` 挡着，其它平台一定走内置下载；内置下载没有续传，也没有实测记录。
- **curl `-m 900` 是单次调用的 900 秒上限**：超大 mod 在慢网络下可能超时，重试会从断点继续，但脚本要允许较长时间。
- **体积相符即视为已下载**：如果 `dl` 里存在一个字节数碰巧相同的坏文件，会被当成下好的文件跳过（没有校验和/哈希）。
- **队列的自动安装同样依赖导入器注入**，命令行下永远停在 `ready-to-install`；`--delete-after` 因此也删不掉文件。
- **没有「已安装」记录与搜索列表的联动**：队列里是 `installed` 只发生在网页界面那条路。
- 黑名单没有「暂时停用」开关，只能删掉再加回来。
