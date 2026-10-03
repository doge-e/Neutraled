# 管理与扩展：配置档 / 快照 / 恢复点（MANAGE）

> **本篇包含**（按顺序）：
> - [管理与扩展（MANAGE）](#管理与扩展manage) — 管理与扩展总览（速查）
> - [配置档（PROFILES）](#配置档profiles) — 配置档：切换启用集合与游戏设置
> - [快照（SNAPSHOTS）](#快照snapshots) — 单 mod 版本快照
> - [恢复点（RESTORE）与黑名单（BLACKLIST）](#恢复点restore与黑名单blacklist) — 整游戏恢复点与黑名单

## 管理与扩展（MANAGE）

本文覆盖 Neutraled 的管理类功能：**配置档 / 快照 / 恢复点 / 黑名单 / GameBanana 下载 / 插件 / 主题 / 语言包 / 网页界面 / 跨平台**。
它们全部走同一个 CLI 进程（`ntl-builder`），不依赖图形界面，因此可以在服务器、CI、macOS/Linux 上原样运行。

> 约定：所有命令都可以加 `--lang en` 切换输出语言（或写进 `config.json` 的 `lang`）。
> 主开关后面、下一个 `--` 之前的记号是**位置参数**；可选参数一律是具名开关（`--name`、`--out`、`--force` …）。
> 退出码：`0` 成功、`1` 失败、`2` 用法/参数错误、`3` 被拒绝（例如游戏正在运行）。

---

### 1. 数据布局

| 路径 | 内容 |
| --- | --- |
| `Neutraled/profiles/<id>.json` | 一个配置档：启用集合 + 一次性写的游戏设置 |
| `Neutraled/profiles/active.json` | 当前活动档指针的**镜像**（权威值在 `config.json` 的 `active_profile`；本文件供外部工具读取，两者不一致时以 `config.json` 为准） |
| `Neutraled/snapshots/<modId>/<version>/…` | 某个 mod 的版本快照（**真实复制**，独立 inode —— 绝不硬链接，否则就地改 live 会连备份一起改）+ `.snapshot.json` 元数据 |
| `Neutraled/restore/<pointId>/manifest.json` | 整游戏恢复点：各章节 data.win 的清单 + 数据副本 |
| `Neutraled/blacklist.json` | 黑名单（按 id / 名称 / 类别屏蔽搜索结果） |
| `Neutraled/dl/queue.json` + 同目录文件 | 下载队列与已下载的压缩包 |
| `Neutraled/plugins/<目录>/plugin.json` | 插件清单 + 入口 DLL |
| `Neutraled/themes/<id>.json` | 外部主题（内置 dark/light/high-contrast/classic-deltarune） |
| `Neutraled/console-theme.json` | **游戏内**控制台正在用的配色（由 `--theme-use` 写出，GML 侧启动时读） |
| `Neutraled/lang/lang_<code>.json` | 外部语言包 |

---

### 2. 配置档 profile

一个配置档 = 「哪些 mod 启用」+「跟随这次切换写入的一组设置」。应用配置档 = 直接改各 `mods/<id>/mod.json` 的 `enabled` 字段（与 GUI 的勾选框改的是同一处）。

```powershell
ntl-builder --profile-new 剧情 --name "只看剧情" --from default   # 以现有档为底
ntl-builder --profile-list                                        # 列出全部档 + 当前生效档
ntl-builder --profile-show 剧情                                   # 看某个档的启用集合
ntl-builder --profile-use 剧情                                    # 应用（改 mod.json 的 enabled）
ntl-builder --profile-copy 剧情 剧情-测试 --name "剧情（实验）"
ntl-builder --profile-rename 剧情 story
ntl-builder --profile-delete 剧情-测试 [--force]
ntl-builder --profile-export 剧情 [--out out.json]
ntl-builder --profile-import out.json [--force]
```

`--profile-use` 输出「改动 N 个 mod」；同 id 已存在时 `--profile-new` **不覆盖**并返回退出码 2（用 `--profile-copy` 或换 id）；`--profile-delete` 删活动档需要 `--force`，否则返回 2。

### 3. 每 mod 版本快照 snapshot

给单个 mod 的目录做时间点副本：`--snapshot-create` 记下 **版本 + 备注 + 目录树哈希**，之后可以原地切回。

```powershell
ntl-builder --snapshot-create konami --version 1.2.0 --note "加入双人模式"
ntl-builder --snapshot-list konami
ntl-builder --snapshot-use konami 1.2.0 [--force]     # 切回（当前内容先自动存一份保底快照）
ntl-builder --snapshot-import konami D:\konami-1.1.zip --version 1.1.0
ntl-builder --snapshot-delete konami 1.1.0 [--force]
ntl-builder --snapshot-auto                             # 给**全部已装 mod** 各建一份保底快照
```

> ⚠ **磁盘**：`--snapshot-auto` 会为每个 mod 建快照。实测本机 67 个 mod（含大量测试 mod）占用约 **2.7 GB**。生产机器上按需使用；同一卷内用硬链接时几乎不占空间，跨卷会退化成复制。

### 4. 整游戏恢复点 restore

把「整个游戏当前的 mod 状态」存成一个点：逐章 data.win 的哈希 + 元数据 + 数据副本。

```powershell
ntl-builder --restore-create [--name "通关前"] [--from live|profile] [--no-backup]
ntl-builder --restore-list
ntl-builder --restore-apply rp-20260926-120641 [--force]
ntl-builder --restore-export rp-20260926-120641 D:\backup\before-boss.ntlrestore
ntl-builder --restore-import D:\backup\before-boss.ntlrestore [--force]
ntl-builder --restore-delete rp-20260926-120641 [--force]
```

* `--restore-apply` 在 **DELTARUNE 正在运行**时拒绝执行（返回 3），并会先自动写一个 `auto-<时间戳>` 回退点，避免误操作不可逆。
* `--no-backup` 只存清单与哈希（快、省空间），代价是无法用这份点还原内容。
* 有数据副本的恢复点体积 ≈ 各章 data.win 之和（实测 6 个文件约 **509 MB**）。

### 5. 黑名单 blacklist

在 `--gb-search` 的结果里屏蔽不想要的条目（按 GameBanana id、名称子串或类别）。

```powershell
ntl-builder --block-add 123456 --kind id --note "换皮包"
ntl-builder --block-add "NFT" --kind name
ntl-builder --block-list
ntl-builder --block-remove 123456 --kind id
```

### 6. GameBanana 浏览与下载队列

```powershell
ntl-builder --gb-search "pizza tower" [--per-page 30]
ntl-builder --gb-files 123456 [--gb-type Mod]
ntl-builder --gb-install 123456 [--file 0] [--chapter chapter3] [--force]   # 下载 + 自动识别格式 + 导入
ntl-builder --queue-add 123456 --name "某 mod" [--file 1] [--url 直链] [--size 字节]
ntl-builder --queue-list
ntl-builder --queue-run [--no-install] [--delete-after]
ntl-builder --queue-remove <条目id> [--delete-file]
```

* 下载优先用系统 `curl`（支持断点续传、4 次重试）；非 Windows 平台或没有 curl 时回退到内置 HTTP 客户端。
* 队列状态机：`queued → downloading → downloaded → ready-to-install → installed`（失败 / 取消有独立状态）。
* `--gb-install` 会调用与 `--import-mod` 相同的导入器（含 Kristal 转换），因此 **导入结果与手动导入完全一致**（CLI 与 `--web` 两条路径都会注入导入钩子）。
* `--queue-run` 默认下载完就安装；`--no-install` 只下载（状态停在 `ready-to-install`）。

### 7. 插件与钩子

插件是 .NET 类库（`.dll`），宿主用 `AssemblyLoadContext`（可卸载）加载；清单与 DLL 放在 `Neutraled/plugins/<目录>/`。

#### 7.1 plugin.json

```json
{
  "id": "sample-hello",
  "name": "示例插件",
  "version": "1.0.0",
  "author": "you",
  "description": "最小的 Neutraled 插件",
  "entry": "SampleHello.dll",
  "type": "SampleHello.HelloPlugin",
  "permissions": ["mods.read"],
  "requires": [],
  "conflicts": [],
  "min_builder": "1.0.0"
}
```

| 字段 | 说明 |
| --- | --- |
| `id` | 唯一标识；**必须与插件目录名逐字符相同**（不一致会被判为清单损坏并拒绝加载/写盘，防目录穿越） |
| `entry` | 入口 DLL 文件名（相对插件目录） |
| `permissions` | `files.read` / `files.write` / `mods.read` / `mods.write` / `proc.run` / `net.http` / `game.data` |
| `requires` | 依赖的其它插件 id（决定加载与钩子调用顺序） |
| `conflicts` | 与之互斥的插件 id |
| `min_builder` | 需要的最低 builder 版本 |

#### 7.2 接口

```csharp
public interface INeutraledPlugin
{
    string Id { get; }
    void OnInit(IPluginContext ctx);
    void OnShutdown(IPluginContext ctx);
    int OnHook(string hook, IPluginContext ctx);   // 非 0 = 否决/中止该操作
}

public interface IPluginContext
{
    string GameRoot { get; }
    string NeutraledRoot { get; }
    string PluginDir { get; }
    IReadOnlyList<string> Permissions { get; }
    string Language { get; }
    string? Data { get; }            // 钩子负载（JSON 文本）；OnInit/OnShutdown 为 null
    void SetResult(string json);
    void Log(string message);
    void Warn(string message);
    bool HasPermission(string perm);
    string? ReadConfig(string key);   // 需要 files.read
    void WriteConfig(string key, string? value);  // 需要 files.write
}
```

钩子名（稳定标识，不参与 i18n）：`before_deploy`、`after_deploy`、`before_import`、`after_import`、`on_command`、`before_exit`。
`before_deploy` / `before_import` 返回非 0 会**中止**该次部署/导入并把 `SetResult` 的内容告诉用户；`after_*` 的返回值不影响流程。

#### 7.3 命令

```powershell
ntl-builder --plugin-list
ntl-builder --plugin-info sample-hello
ntl-builder --plugin-enable sample-hello
ntl-builder --plugin-disable sample-hello
ntl-builder --plugin-install <目录或 .ntlplugin 包> [--force]
ntl-builder --plugin-remove sample-hello [--force]
ntl-builder --plugin-hooks         # 加载全部插件并打印钩子调用记录（hook/rc/id/耗时）
```

权限是**协作式**的：未声明的 `ReadConfig`/`WriteConfig` 一定被拒并写日志，插件也不能借此访问任意文件路径——但它不是安全沙箱，别装来路不明的 DLL。

仓库自带一个最小示例：`Neutraled/plugins/sample-hello/`（清单 + 源码）。

### 8. 主题

内置四个：`dark`（默认，与改造前像素级一致）、`light`、`high-contrast`、`classic-deltarune`。

```powershell
ntl-builder --theme-list
ntl-builder --theme-use light        # 同时写出 Neutraled/console-theme.json（游戏内控制台下次启动生效）
ntl-builder --theme-show light       # 打印主题 JSON（可导出为文件）
ntl-builder --theme-import my.json [--force]
```

外部主题（`Neutraled/themes/<id>.json`）：

```json
{
  "schema": 1, "id": "mine", "name": "我的配色",
  "colors":     { "bg": "#101014", "panel": "#181820", "fg": "#e8e8f0", "dim": "#7a7a88",
                  "border": "#3a3a4a", "accent": "#ffd75f", "highlight": "#c8c8d8",
                  "selection": "#ff9f43", "ok": "#7bd88f", "warn": "#ff9f43", "error": "#ff5f5f" },
  "colors_bgr": { "bg": 1315856, "panel": 2103320 }
}
```

11 个颜色键固定为 `bg panel fg dim border accent highlight selection ok warn error`。游戏侧 `ntl_theme_c(name)` 优先用 `colors_bgr`（GameMaker 颜色是 BGR 整数，C# 侧顺手算好，游戏不必解析十六进制）；缺键自动回退内置默认，读不到文件时配色与改造前完全一致。

### 9. 语言包

界面语言 = 内置 `zh` / `en`（编译期表）+ 外部语言包 `Neutraled/lang/lang_<code>.json`。
随项目提供 6 种：内置 `zh`、`en`，外部包 `de`、`es`、`fr`、`ja`（`Neutraled/lang/lang_<code>.json`）。
早期还随包 `ko` / `ru` / `zh-TW`，因这三种语言的正文没有可用的游戏风格字形（谚文/西里尔/繁体汉字）已于 2026-09-26 移除，见 [THEMES-LANGS.md](THEMES-LANGS.md) §1 的说明。

```json
{ "code": "ja", "name": "日本語", "map": { "部署": "デプロイ", "配置档": "プロファイル" } }
```

* key = **中文原文**（与 C# 的 `L("中文", …)` 一致）。**空译文与「译文 == 中文」的占位一律丢弃**，于是缺键时自动回退中文，绝不会显示空白。
* 游戏内（GML 控制台/HUD）由 `api/ntl_lang_pack.gml` 在启动时读取同一个文件，把「符号键 → 中文」的 zh 表与语言包合成新表，因此 `ntl_t`/`ntl_ts`/`ntl_tf` 一行不改就能说任意语言。

```powershell
ntl-builder --lang-list                  # 可用语言 + 当前语言
ntl-builder --lang-use ja                # 切换并存进 config.json（游戏内下次启动生效）
ntl-builder --lang-coverage [ja]         # 覆盖率：C# 侧 与 游戏内 两个域分开统计；退出码 0 = 全部 100%
ntl-builder --lang-template ru --out ru.json   # 导出待译模板（含英文参考）
```

新增一种语言的流程：`--lang-template <code>` 导出 → 填 `map` → 存成 `Neutraled/lang/lang_<code>.json` → `--lang-coverage <code>` 看还缺哪些。

### 10. 网页界面（跨平台前端）

```powershell
ntl-builder --web [--port 7931] [--no-open] [--auto-stop]
```

* 只绑 `127.0.0.1`，端口被占用时自动 +1 重试（最多 6 次）；零 CDN 依赖，静态页随包分发（`web/`）。
* 面板：状态 / mod 列表与开关 / 配置档 / 快照 / 恢复点 / GameBanana 搜索与安装 / 下载队列 / 插件 / 主题 / 语言 / 部署。
* 接口（JSON）：`GET /api/status|mods|profiles|snapshots|restore|queue|plugins|themes|lang`、`GET /api/gb/search`、`GET /api/lang/coverage`、`POST /api/mods/toggle|profile/use|profile/save|snapshot/use|restore/apply|gb/install|queue/run|theme/use|lang/use|deploy|shutdown`。
* `POST /api/deploy` 需要宿主注入部署钩子（CLI 已注入）；单独嵌入 `WebUi` 时未注入会返回 501。

### 11. 游戏内 Mod 设置面板（CONFIG 菜单第 6 行）

游戏自己的 **CONFIG（设置）菜单里会多出一行** `Mod 设置`，插在 `Auto-Run` 与 `Return to Title` 之间，
样式与官方项完全一致（同一套缩进、白字、选中红心）：

```
Master Volume   60%
Controls
Simplify VFX    OFF
Fullscreen      OFF
Auto-Run        ON
Mod 设置        3 个模组       ← 光标移到这里按 Z / Enter
Return to Title                ← 原版第 6 行，顺移到第 7 行
Back                           ← 原版第 7 行，顺移到第 8 行
```

> 为什么不加在最后一行：加在 `Back` 后面会**画在菜单框外面**，看着像外挂上去的一块（早期版本就是这样）。
> 现在插在官方行中间，行高、缩进、字色、红心全部沿用官方那一套（`set.row` / `set.value` 两个 i18n 键）。
>
> 右列数字是**本产物实际加载**的 mod 数（部署时写进产物目录的 `Neutraled/mods.json`），不是磁盘上装了多个：
> 原版基底产物显示 `0 个模组`，面板第二行的说明行会同时给出「磁盘上已安装 N 个」。
>
> **菜单框保持原版高度**（`langopt([90, 410, 420], [85, 412, 422])`，与官方一字不差）。
> 多出来的第 8 行靠**滚动**容纳：光标走到第 7 项时整栏上移一行，右侧出现细滚动条，
> 原来的 `Back` 就回到视野里（见 §11.4）。早期版本是把整个框加高 36 px，那样窗口会跟着变高，已经废弃。

打开后**不是自绘面板**，而是游戏自己的二级菜单（原生 submenu 通道，id = 51）：
黑框与星角边框由游戏本身画，行高 35、选中红心 sprite（**按名字解析** `spr_heart`：chapter4=`3695` / chapter1=`922`，
见 §11.4 教训 5）、底部说明行都与官方二级菜单一致；
字体也换成官方菜单用的 `mainbig`（不再切小一号的内置 CJK 字体，见 §11.4）。

| 行 | 做什么 |
| --- | --- |
| 章节选择 | 列出已注册章节（官方 / 补丁 / 时间线 / 外部）与启用状态 |
| 已加载模组 | 本产物实际加载的 mod 数（读产物目录里的 `Neutraled/mods.json`；磁盘上装了多个在说明行里给出，只读） |
| 界面语言 | `←`/`→` 直接快切；`Z` 进语言列表，`*` 标记当前语言，立即生效 |
| 重新部署并重启 | 写 `Neutraled/launch-request.json` 后退出游戏，交给守候进程重新部署 |
| （mod 注册项） | 装了的 mod 可以在这里加自己的开关，见 [docs/MODMENU-API.md](MODMENU-API.md) |
| 关闭面板 | 回到设置菜单（按 `X` 同效） |

键位与官方菜单一致：`↑` `↓` 选择（循环；超过 5 行自动滚动，右侧细滚动条提示）、`Z` / `Enter` 确认、`X` 返回；
底部说明行随光标显示「这一行是干什么的」——它同时也是消息行（例如「已请求重新部署…」）。

* **依赖部署补丁**：这一行是部署时往 `data.win` 里打的补丁，部署日志里应能看到：
  `增强: 设置菜单竖直循环(上, 8 项)`、`增强: 设置菜单竖直循环(下, 8 项)`、
  `增强: 设置菜单第 8 项(解夹-判断)`、`增强: 设置菜单第 8 项(解夹-赋值)`、
  `增强: 设置菜单行下移(Return to Title)`、`增强: 设置菜单行下移(Back)`、
  `增强: 设置菜单第 6 行绘制(Mod 设置)+红心跟随滚动+滚动条`、`增强: Mod 设置面板绘制(submenu 51)`、
  `增强: 设置菜单第 6/7/8 行按键接管`、`增强: 设置菜单第 6 行按键(改由 Neutraled 分派)`、
  `增强: 设置菜单第 7 行按键(改由 Neutraled 分派)`、`增强: Mod 设置面板输入(submenu 51)`、
  `增强: 设置菜单滚动偏移(_ntl_cfg_off)`、`增强: 设置菜单滚动(第 1..5 行 …)`（共 11 条，把官方 8 行整体上移）。
  所以**每个要用的章节都得单独部署一次**（`--deploy --chapter chapter4`；`--deploy-all` 会一次做完 root + 各章）。
  > `增强: {标签}` 只是「补丁已入队」的回执，**不代表 search 命中**（见 §11.4 的教训）；
  > 真正把关的是部署自检 (g)「界面增强标记」。
* **「重新部署并重启」需要守候进程**：和 `--watch-external` 是同一套（见 [PIPELINE.md](PIPELINE.md) 与
  [WEBUI.md](WEBUI.md)），也就是桌面程序里点「部署并启动」之后留在后台的那个进程；
  没有它时这一行会提示「需要 Neutraled 守候进程在运行」，其它行照常可用。
* **语言**：面板文案和其它界面共用 `Neutraled/lang/lang_*.json` 的 `menu.*` 键（六种语言随包分发）。
* 实现文件：`api/ntl_settings_row_draw.gml`（设置页那一行的绘制，注入 `obj_darkcontroller` 的 Draw）、
  `api/ntl_settings_row_press.gml`（那一行的按键分派，注入同对象的 Step）、
  `api/ntl_modmenu_open.gml` / `api/ntl_modmenu_close.gml`（进/出面板 = 切 `global.submenu` 与光标槽位）、
  `api/ntl_modmenu_page_draw.gml` / `api/ntl_modmenu_page_step.gml`（面板页的绘制与输入）、
  `api/ntl_modmenu_row.gml` / `api/ntl_modmenu_count.gml` / `api/ntl_modmenu_slot.gml`（行数据 / 行数 / 光标槽位）、
  `api/ntl_modmenu_step.gml`（消息与退出倒计时）、`api/ntl_modmenu_deploy.gml`（部署请求）。
* **参考实现（历史）**：整包 mod `mods/deltarune_60_fps`（BadArtAdventure）的「MOD SETTINGS」页走的就是这条路
  （它占 `global.submenu == 50`，我们用 51）：行布局、红心、说明行都取自它（它把框加高来塞加行，我们改成滚动，见 §11.4）——
  官方更新后照着它核对即可。
  > ⚠ **现在这个页面已经进不去了**（2026-09-27）：60fps 被转成源码级差异层后，它的菜单对象
  > `gml_Object_obj_darkcontroller_Draw_0/_Step_0` 与我们的注入点撞车，被写进层的 `patch_skip`
  > （见 [PIPELINE.md](PIPELINE.md) 路线 2 的「层不许覆盖 Neutraled 自己补丁的对象」）——
  > 所以它只作为**布局参考**留在源码树里（`mods/deltarune_60_fps/badartadventure/<章>/ref/data.win`
  > 与 `mods/60fps_layer/badartadventure/<章>/LAYER_REPORT.txt`），不再是运行时可看到的界面。
* 已知限制：输入用的是游戏自己的按键 API（`up_p()` / `down_p()` / `button1_p()` / `button2_p()`），
  所以和官方菜单一样只认键盘，不再需要额外的「窗口在前台」判断。

### 11.1 面板里的语言切换与 mod 开发者接口

- **语言**：面板主列表的「界面语言」一行，`←`/`→` 直接快切（切换后立即生效，无需重开游戏）；
  按 `Z` 进入语言列表，用母语名字（中文 / English / 日本語 / Deutsch / Español / Français）列出全部可选语言，
  `↑↓` 选、`Z` 确定（`*` 标记当前语言）、`X` 返回。选择会写回 `Neutraled/config.json` 的 `lang`。
- **mod 开发者接口**：任何 mod 都可以往这个面板里加自己的项（标签 / 数值 / 动作脚本，支持多语言文案），
  见 [docs/MODMENU-API.md](MODMENU-API.md)。

### 11.2 字体补全（内建多语言字体支持）

中文/日文/俄文这类文本能不能画出来，取决于**当前字体的字形表**里有没有这些字：
原版 `fnt_main` 只有 96 个 ASCII 字形，一旦某章的 `lang\lang_*.json` 换成中文，
整段传说文本会一个字都画不出来（屏幕上只剩打字指示器的三个点）——这是本项目第一起事故。

所以**部署时自动补字**（不需要 mod 作者做任何事）：

* 需要的字符集来自**这个产物真正会用到的文本**（`builder/ContentCheck.cs` 的 `AddNeutraledUiChars`，与部署自检共用）：
  1. 同目录 `lang\lang_*.json` 的**全部字符串值**（章节文本；root 取 `api/ntl_i18n_init.gml` 与 `chapters.json`）；
  2. `api/**/*.gml` 里的**界面文案字符串字面量**（Neutraled 自己的面板/提示）；
  3. **`mods/**/gml/*.gml` 里的字符串字面量**（mod 作者写给「Mod 设置」面板的标签/说明也会被这两个字体画出来；
     只扫 `gml/`，**不扫 `patches/`** —— 反编译代码里的字面量多是游戏内部标记，算进去只会拉低覆盖率判定）；
  4. 所有已启用 mod 的 `files/lang_*.json`；5. `lang/lang_*.json`（4 份）；6. `chapters.json` 的章节名。
  ⇒ **别在运行时拼文案**（`"速度 " + string(n)` 这种扫不到，画出来是空洞），字面量写在 `gml/` 里。
* 目标字体是 `fnt_main` / `fnt_mainbig` / `fnt_small` / `fnt_legend`（游戏四种正文/说明字体）。
* 缺的字从内置字体包 `fonts/ntl_font_cjk.json`（**4974 字形 / 4 张位图页** `ntl_font_cjk_o1..o4.png`；2026-09-30 全量重渲；字形全部由 **OFL 1.1 开源字体**（Noto Sans SC/JP/KR/Symbols 2/Emoji）离屏渲染，来源与许可见 `fonts/OFL-NOTICE.txt`）里裁出来补：
  各个 sheet 整页贴到新页 (0,0)（旧字形坐标全部保持有效；本包是 `Page=""` 的**多 sheet 模式**，导入端会重新拼页），新字形按 `目标 EmSize / 包 EmSize` 等比缩放后货架式摆在下方的空白带，
  字形表按 `Character` 升序重排（GMS2 运行期是二分查找，乱序会成片丢字）。
* 只补**真的缺**的：纯英文产物一个字形都不补，产物大小不受影响。
* 部署日志里能看到：

  ```
  字体补全: 文本来源 lang_en.json 的全部字符串值 + api/**.gml 界面文案 + mods/**/gml/*.gml mod 文案（2738 个字符）
  字体补全 fnt_main: +2795 字形（96 -> 2891；页 2048x2048 -> 2048x4096，缩放 1x）
  字体补全 fnt_mainbig: +2795 字形（96 -> 2891；页 2048x2048 -> 2048x4096，缩放 2x）
  ```

  ```powershell
  ntl-builder.exe --font-probe chapter4_windows\data.win   # 复核字形数与字形表自检（逆序对应为 0）
  ```

* **分工**：Neutraled 只负责**字体/字形层面的支持**（能显示、不缺字、不缺标点）；
  具体文本的**翻译**由人工本地化组维护，随包分发的 `lang\lang_*.json` 只是参考译法
  （见 [THEMES-LANGS.md](THEMES-LANGS.md) 与 `--lang-coverage`）。
* **许可**：内置字体包的字形由 **OFL 1.1** 开源字体（Noto Sans SC / JP / KR / Symbols 2 / Emoji，以及非中文字形用的 Ark Pixel 12px）离屏渲染；随包分发 `fonts/OFL-NOTICE.txt`（逐张页的来源/版本/SHA-256/版权行）与 `fonts/ofl/` 下的 6 份 OFL 全文。
* 实现：`builder/FontMerge.cs`（补字）、`builder/ContentCheck.cs`（算出需要哪些字符，与部署自检共用一份口径）、
  `builder/FontImport.cs`（写字体页与字形表）、`builder/CjkFont.cs`（生成内置包 `--make-cjk-font`）。
### 11.3 字距（CJK 排字步进）

补上字形只是「画得出来」；画得**对不对**还取决于游戏排字时每个字前进多少像素。

原版 `obj_writer` 的 Draw 事件里，非 ASCII 字符一律按固定格子推进：

```gml
if (ord(mychar) > 505 || ord(mychar) == 183) { wx += ((hspace * 7) div 4); }   // 8 * 7 / 4 = 14
else { wx += hspace; }                                                          // hspace = 8
```

这套数值是按原版 8-bit 字体（ASCII 约 6 px 宽）定的：小字 `fnt_main` 的汉字只有 11 px 宽，
14 px 的格子刚好放得下；**大字 `fnt_mainbig` 的汉字有 22 px 宽**，14 px 的格子会吃掉 8 px，
于是整行汉字互相重叠、糊成一片（第一章传说「高高立于王国的中心。」肉眼不可读）。

所以部署时顺带改成「按当前字体下该字符的实际宽度推进」：

```gml
wx += hspace;
if (global.lang != "ja" && ord(mychar) > 255)
{
    var _ntlw = (string_width(mychar) * max(textscale, 1)) + 2;
    if (hspace < _ntlw) { wx += (_ntlw - hspace); }
}
```

* 取「原固定格子」与「实际字宽 + 2 px 间隙」的**较大者** ⇒ 纯英文/日文产物的排版**一个像素都不变**，
  只有汉字/西里尔这类宽字形才会被撑开。
* `textscale` 兜底为 1（电话等小字号场景它是 0.5）。
* 只改 `global.lang != "ja"` 的分支：日文模式原版自己按日文字体调过，不动它。
* 补丁点在 `builder/Injector.cs` 的 2.8 节（`obj_writer_Draw_0`），日志里显示为
  `增强: 文本字距(CJK 按实际字宽推进)`。

**验收方法**（改完必须看真机，不能只看产物里有没有代码）：

```powershell
ntl-builder.exe --deploy --chapter chapter1 --game <游戏根> --no-launch --force
ntl-builder.exe --dump gml_Object_obj_writer_Draw_0 --chapter chapter1 | Out-File -Encoding utf8 dump.txt
```

dump 里要能看到 `if (hspace < _ntlw)`；然后真机截图 + 像素测量字距（脚本 `E:\aiwork\out\Neutraled2\measure-pitch.ps1`：
按 y 带找墨迹、算自相关峰值 lag）。修复前后对照：墨迹宽 312 px → 600 px，步进 lag 12 px → 64 px（屏幕像素）。

### 11.4 字号与滚动条（不伸缩窗口）

早期版本为了让「Mod 设置」这一行有地方放，把整个 CONFIG 菜单框**加高 36 px**
（`langopt([90, 446, 456], [85, 448, 458])`）。坏处是弹出来的窗口比官方高一块，和周围界面不合群。
现在**菜单框保持原版尺寸**，多出来的行改用滚动容纳；同时把这一行与面板的字号换成官方菜单字号。

**字号**：官方暂停菜单的行是 `draw_set_font(7)` 画的，按 `scr_84_init_localization` 的英文映射
`mainbig = 7` ⇒ 就是游戏自己的 `fnt_mainbig`（EmSize 24）。
旧实现遇到中文会切到内置的 `ntl_font_cjk`（EmSize 12），所以看着比官方小一半。
现在统一走游戏字体：

* `api/ntl_font_big.gml` → `asset_get_index("fnt_mainbig")`（失败再回退 `global.font_map[? "mainbig"]`，都不行返回 -1）
* `api/ntl_font_main.gml` → `fnt_main`（面板底部说明行用它）
* 前提是这些字体已经被**补过 CJK 字形**（见 §11.2）——所以字符集里必须包含 **Neutraled 自己的界面文案**
  （`api/**/*.gml` 的字符串字面量 + **`mods/**/gml/*.gml` 的 mod 文案** + `lang/lang_*.json` + `chapters.json` 的章节名），
  否则换成官方字体后，面板上的「已加载模组」「章节选择」反而会变成空格。
  `builder/ContentCheck.cs` 的 `AddNeutraledUiChars()` 就是干这个的，章节分支与 root 分支各调一次。

**滚动**（三个 api 文件 + Injector 注入的偏移量）：

| 文件 | 作用 |
| --- | --- |
| `api/ntl_cfg_scroll.gml` | `clamp(global.submenucoord[30] - 6, 0, 1)` ⇒ 光标在第 8 项时才滚动一行 |
| `api/ntl_cfg_row.gml` | 行号不在 `off .. off+6` 窗口内就 `draw_set_alpha(0)`，避免框外「鬼影行」 |
| `api/ntl_cfg_scrollbar_draw.gml` | 框内右侧细滚动条（轨道 `xx+572`、`yy+150..yy+384`，滑块高 7/8 轨道） |

注入侧在 `var _xPos = …` 那句后面插一行 `var _ntl_cfg_off = ntl_cfg_scroll() * 35;`，
然后把官方 8 行（含各自的数值列）的 y 全部改成 `(yy + <原值>) - _ntl_cfg_off`，红心改成
`yy + 160 + ((global.submenucoord[30] - ntl_cfg_scroll()) * 35)`。滚动条跟着「Mod 设置」行的绘制一起画
（同一条 replace 的尾部追加 `draw_set_alpha(1); ntl_cfg_scrollbar_draw(xx, yy);`）。

**产物里被改到的代码不是同一份**（这是踩过的最大一个坑）：

| | chapter4 | chapter1 |
| --- | --- | --- |
| 行文本 | `stringsetloc("Return to Title", "…_95_0")` | `scr_84_get_lang_string("…_95_0")` |
| 红心 sprite | `3695` | `922` |
| 框高写法 | `var lang_off = langopt([90, 410, 420], [85, 412, 422])` | 内联 `draw_rectangle(xx+60, yy+90, xx+580, yy+410)` |

⇒ 注入补丁的 `search` 必须只用**两个变体共有**的短锚点（例如 `draw_text(_xPos, yy + 325,`），
不要带 `stringsetloc` / sprite 号 / `lang_off` 这类变体特征。

⇒ 这张表里的**数字一个都不能被 `api/**` 的绘制代码直接引用**：`api` 是同一份、要塞进 6 个章节产物的，
写死 `3695` 就等于写死 chapter4（真机事故见下面教训 5）。红心一律走 `ntl_heart_sprite()`（按名字解析）。

**五条必须记住的教训**：

1. `增强: {标签}` 是「补丁已入队」的回执，**跟 search 有没有命中无关**（`builder/Injector.cs` 里
   `QueueFindReplace` 之后无条件 `Paths.Log("增强: …")`）。日志全绿、产物里却一个字都没改，是真实发生过的事。
   所以部署自检有 (g)「界面增强标记」：反编译产物的 `obj_darkcontroller` Draw/Step，
   断言 `ntl_settings_row_draw` / `ntl_cfg_scroll(` / `ntl_cfg_row(` / `ntl_cfg_scrollbar_draw` /
   `ntl_modmenu_page_draw` / `ntl_settings_row_press` / `ntl_modmenu_page_step` 七个标记都在，缺一个就判失败。
2. **不要让一条 FR 的 `search` 指向前一条 FR 插入出来的文本**：实测这种跨 FR 依赖会静默不命中
   （同一个代码条目内、同一批 `QueueFindReplace` 里也不保证看得到彼此的插入结果）。
   需要追加的东西，直接写进**同一条 replace** 的尾部。
3. **api 里的局部变量必须先 `var` 声明**：`ntl_cfg_row.gml` 写了 `var _vis = (_k >= _off && …)`
   却漏掉 `var _k = argument[0];`，真机一进 CONFIG 页就弹
   `Variable obj_darkcontroller._k(115339, -2147483648) not set before reading it.`
   （GML 不报编译错，lint 规则 16「局部变量未声明」+ doctor 规则 4 现在共用同一套扫描，能提前抓住；
   顺带抓到 `ntl_love_host.gml` 用了没声明的 `_a4`）。
   注意别用「出现过赋值就算定义」的粗判 —— `with (obj) { _x = 1; }` 这种裸实例变量赋值是合法的。
4. **`draw_set_alpha` 会被最后画的那一行留在 0**：行可见性靠 `draw_set_alpha(0/1)` 实现，
   而面板绘制在行之后 ⇒ 光标不在滚动位置时，最后画的第 8 行把 alpha 留成 0，
   打开面板会**整片透明**。修法是面板那条 FR 的 replace 前面加无条件的 `draw_set_alpha(1);`，
   分支里再置一次（`draw_set_alpha(1); if (global.submenu == 51) { draw_set_alpha(1); ntl_modmenu_page_draw(xx, yy); } …`）。
5. **资源索引不许写死**：精灵（字体、音效同理）的**索引只是那一份 `data.win` 里的序号，换章节就变** ——
   同一张 `spr_heart`，chapter4 是 `3695`、**chapter1 是 `922`**。`api/ntl_modmenu_page_draw.gml` 早期直接写了
   `draw_sprite(3695, 0, _heartXPos, _yy + 160 + ((_coord - _scroll) * 35));`，于是 **chapter1** 里一开 Mod 设置面板就弹：

   ```
   ERROR in action number 1 / of Draw Event / for object obj_darkcontroller:
   Trying to draw non-existing sprite.
   at gml_Script_ntl_modmenu_page_draw
   ```

   修法是按**名字**取：`api/ntl_heart_sprite.gml` 里 `asset_get_index("spr_heart")` + `sprite_exists()` 守卫，
   取不到就兜底 `draw_rectangle` 画个 16×16 红块（不能只是跳过，否则光标看不见）；运行期留一行诊断，
   真机 chapter1 实测输出 `[ui] [面板] 红心精灵解析: spr_heart=922(16x16) spr_heartsmall=924(9x9) … ⇒ 选用 index=922 name=spr_heart`。
   同类先例是字体（`api/ntl_font_big.gml` 用 `asset_get_index("fnt_mainbig")`）。lint 规则 17「资源索引写死」
   现在会扫 `api/**` + `live/**` 里的裸索引（`draw_sprite(3695, …)` / `sprite_index = 922`）；
   确实要按索引写时，在文件里加标记 `ntl:raw-asset-ok`。

**验收**：

```powershell
ntl-builder.exe --deploy --chapter chapter1 --game <游戏根> --no-launch --force
# 日志里要有：[通过] (g) 界面增强：设置菜单/面板补丁落在产物里 —— 7 项标记全部命中
ntl-builder.exe --deploy-all --game <游戏根> --no-launch --force     # 6/6 成功
ntl-builder.exe --dump gml_Object_obj_darkcontroller_Draw_0 --chapter chapter1 | Out-File -Encoding utf8 dump.txt
```

dump 里应看到 `ntl_cfg_row(`（每行一个）、`_ntl_cfg_off`、`ntl_cfg_scrollbar_draw`、`ntl_modmenu_page_draw`；
然后真机看：字号与官方一致、框高与原版一致、光标推到第 8 项时整栏上移且 `Back` 进入视野。

### 12. 跨平台

`ntl-builder` 目标框架是 `net9.0`（非 `-windows`），Windows / macOS / Linux 同一套代码：

| 平台差异 | 处理 |
| --- | --- |
| 章节目录后缀 | `Platform.ChapterSuffix()` 先探测 `chapter1_windows\_linux\_unix\_macos`，都没有再按 OS 推断 |
| 硬链接（缓存加速 / 快照） | Windows `CreateHardLinkW`、Unix `link()`；跨卷或文件系统不支持时**自动回退复制** |
| 窗口抢焦点（截图/自动化用） | 仅 Windows 实现，其它平台整体退化为空操作（`--focus-test` 会明确提示） |
| 下载 | 有 `curl` 走断点续传，否则内置 HTTP |
| 自包含发布 | `release-pack/make-release.mjs`（跨平台打包器，Windows 用 `make-release.ps1`、macOS/Linux 用 `tools/package-unix.sh` 调用；产物 = 只有安装器的 zip）；旧的 `tools/package.ps1` 仍在但会**先删后建**整个发布目录，不推荐 |

自检命令：

```bash
ntl-builder --platform-info      # OS / Arch / Runtime / 章节目录后缀 / 可写性 / 硬链接 / curl 路径
dotnet publish builder/Neutraled.Builder.csproj -c Release -r linux-x64   --self-contained true -o dist/linux
dotnet publish builder/Neutraled.Builder.csproj -c Release -r osx-arm64   --self-contained true -o dist/macos
```

> UTMT 依赖库（`UndertaleModLib.dll` 等）默认从 `<游戏根>/UTMT/` 读，也可用环境变量 `NTL_UTMT_DIR` 指到别处。

### 13. 磁盘占用与安全速查

| 操作 | 实测体积 | 建议 |
| --- | --- | --- |
| `--snapshot-auto`（67 个 mod） | ≈ 2.7 GB | 只在需要时跑；同卷硬链接时不占空间 |
| `--restore-create`（6 个 data.win） | ≈ 509 MB | 大改之前存一个；日常用 `--no-backup` |
| `--restore-apply` | 需游戏关闭 | 会自动先存 `auto-<时间戳>` 回退点 |
| `--profile-use` | 只改 mod.json | 幂等，可反复执行 |
| `--snapshot-use` / `--restore-apply` | 覆盖现状 | 都要 `--force` 才覆盖非空目录；不确定就先 `--snapshot-auto` |

---

## 配置档（PROFILES）

> **一句话**：把「哪些 mod 开着 + 一份游戏设置」存成一个有名字的**配置档**，之后一条命令就能在几套组合之间来回切 —— 切换只改每个 mod 自己 `mod.json` 里的 `enabled`，**不复制、不移动、不删除任何 mod 文件**。

功能概览见 [MANAGE.md](MANAGE.md) §2；本文是配置档的细节文档与实测记录。所有命令都走同一个 `ntl-builder.exe`。

> **关于下面的输出**：本文每个代码块都是**真跑出来的**（把 exe 放进一个沙箱游戏根，路径形如 `E:\aiwork\out\Neutraled2\_feat\docs-c1\sb`，只有演示 mod），这样测试不会动真实仓库。
> 在你机器上第一行会是你的真实游戏根，其余格式完全一致。所有 `[配置档]` 行都写在 **stdout**，只有用法错误写 stderr。

---

### 目的

一个 mod 组合调好之后，下次想换玩法往往要手点十几个勾选框。配置档把这件事变成**一次快照 + 一条命令**：

* 存档 = 一份 `profiles/<id>.json`（启用集合 + 设置 + 涉及章节）；
* 切换 = 逐个改写 `mods/…/mod.json` 的 `enabled`，再把这档记的设置写进 `config.json`；
* 因此 mod 目录、mod 内容、data.win 全都不动 —— 切档是**几毫秒级的元数据操作**，不会重新部署。

实测：切一次配置档（2 个 mod 改状态 + 2 项设置）不到 0.3 秒；**同一档连切两次，第二次输出「改动 0 个 mod，0 项设置」**（幂等，不写盘）。

---

### 快速上手

```bash
# 1) 把「现在的状态」存成一个档（不写 --from 时，以当前实际启用态为起点）
ntl-builder.exe --profile-new mycombo --name "我的组合"

# 2) 看有哪些档、当前活动档是哪个（活动档前面有 *）
ntl-builder.exe --profile-list

# 3) 切换：改各 mod.json 的 enabled + 写这档记录的设置
ntl-builder.exe --profile-use mycombo
```

---

### 目录与文件

```
<游戏根>/Neutraled/
  profiles/
    <id>.json            ← 配置档本体（一个档一个文件）
    active.json          ← {"active":"<id>"}：活动档指针的镜像（给外部工具读）
    <id>.export.json     ← --profile-export 的默认落点（不是配置档本体，列表里不会多出一档）
  config.json            ← 全局配置：active_profile / lang / theme / base_mod / auto_chapter …
  （同一份配置还有存档区镜像：%LOCALAPPDATA%\DELTARUNE\Neutraled\config.json，见下面「config.json 的两份副本」）
  mods/<mod>/<作者>/<目标>/mod.json   ← 启用态的真相就在这里的 "enabled"
```

| 文件 | 作用 |
| --- | --- |
| `profiles/<id>.json` | 一个配置档：启用集合 + 设置 + 章节 |
| `profiles/active.json` | 活动档指针的**镜像**；`config.json` 里的 `active_profile` 才是权威（见「活动档指针」） |
| `config.json` | 档里记的设置会被写到这里；未知键与 `_comment` 原样保留（另有一份存档区镜像，见下） |
| `mods/…/mod.json` | 每个 mod 的 `enabled` 字段；**缺这个字段 = 启用** |

### config.json 的两份副本（GameMaker 文件沙箱）

root 产物（游戏自带启动器 `data.win`，也就是**章节选择器**那一段）带 GameMaker 的**文件沙箱**：

* **写** `program_directory` 开头的 bundle 路径 → 实际落到**存档区** `<game_save_id>Neutraled/config.json`；
* **读** bundle 路径时，只要存档区存在**同名文件**就被它遮蔽（**逐文件**遮蔽：同一次启动读 `Neutraled/mods/` 仍是真实 bundle，因为存档区没有 `mods` 目录）。

章节产物（`chapterN_windows/data.win`）没有这层沙箱，直接用游戏根那份 ⇒ **同一份配置在两个阶段会读到两套内容**。
实测（2026-09-27 探针，日志 `[PROBE] A/B/C`）：root 阶段 `program_directory` 明明是游戏根，读 `Neutraled/config.json` 却拿到存档区那份；
而选择器读到 `lang=en`、章节内读到 `lang=zh`，跳过开关只在一边生效。

现在两边都会自动收敛：

| 谁 | 做什么 |
| --- | --- |
| `api/ntl_config_paths.gml` + `api/ntl_config_load.gml` | **两处都读**（游戏根那份优先覆盖），读完把游戏根那份**镜像**回存档区 |
| `api/ntl_config_set_lang.gml` | 面板里切语言时**两处都写** |
| `builder/ConfigFile.cs`（CLI/GUI） | 每次 `Save` 都 best-effort 镜像到存档区（`Paths.SaveMirrorConfigPath`） |

⇒ 手改**哪一份**都行，下一次启动就会同步；日志里看 `[cfg] 配置来源: …`、`解析字段[1/2 <路径>]`、`已把游戏根 config.json 镜像到存档区: …`。

### 配置档 JSON 字段

一个档不长这样（实测导出的 `docsdemo.json`）：

```json
{
  "id": "docsdemo",
  "name": "文档演示档",
  "description": "MANAGE.md 示例：把当前启用态存成一个档",
  "created": "2026-09-26T12:32:00",
  "updated": "2026-09-26T12:32:33",
  "enabled": [
    "demoa.docs",
    "demob.docs"
  ],
  "settings": {
    "lang": "zh",
    "theme": "dark",
    "auto_chapter": 4
  },
  "chapters": [
    "root",
    "chapter1"
  ],
  "mods_root": "mods",
  "note": ""
}
```

| 字段 | 含义 |
| --- | --- |
| `id` | 档 id（= 文件名去掉 `.json`）；只能含字母/数字（含中文）/ `_ - . @ +`，长度 ≤128，不能含 `..` 或路径分隔符 |
| `name` / `description` / `note` | 显示名 / 说明 / 备注（纯展示，不参与行为） |
| `created` / `updated` | 本地时间 `yyyy-MM-ddTHH:mm:ss`；保存时自动刷新 `updated` |
| `enabled` | 启用 mod 的 id 列表（**不在此列表里的 mod 会被禁用**） |
| `settings` | 应用时要写进 `config.json` 的键值；采集范围 = 内置 8 键 ∪ 档里原本就有的键 |
| `chapters` | 由启用 mod 的章节推导（`root` 排最前），只作展示与恢复点参考 |
| `mods_root` | mods 根，默认 `mods`；只接受 `Neutraled` 之下的相对路径 |

内置会采集的设置键（`Profiles.cs:82-86`，也是打印顺序）：
`lang`、`theme`、`base_mod`、`auto_skip_selector`、`auto_chapter`、`skip_legend`、`debug_live`、`cache_max_mb`。
**特意不含 `active_profile`** —— 它是「当前用哪个档」的指针，不属于任何一个档的内容。

---

### 命令一览

| 命令 | 作用 | 实测退出码 |
| --- | --- | --- |
| `--profile-list` | 列出全部档 + 活动档（`*` 标记） | 0 |
| `--profile-show [id]` | 看某个档的详情；省略 id = 看活动档 | 0（找不到也是 0） |
| `--profile-new <id> [--name 显示名] [--from 已有档id] [--desc 说明]` | 新建；`--from` = 以某档为底，否则按当前实际状态生成 | **恒 0**（非法 id / 重名也是 0） |
| `--profile-use <id>` | 应用（改 `enabled` + 写设置 + 设为活动档） | 0；档不存在 **2** |
| `--profile-copy <源id> <目标id> [--name 显示名]` | 复制一份 | 0；源不存在/目标已存在 **1** |
| `--profile-rename <旧id> <新id>` | 改名（= 换文件名；活动档会被同步） | 0；失败 **1** |
| `--profile-delete <id> [--force]` | 删除档文件（**不动 mod 文件**；活动档/默认档需 `--force`） | 0；被拒/不存在 **1** |
| `--profile-export <id> [--out 文件]` | 导出为自包含 JSON | 0；找不到该档 **2** |
| `--profile-import <文件> [--force]` | 导入（重名要 `--force`） | 0；重名未加 `--force` / 内容非法 **2** |

**位置参数必须写在选项前面**（解析器遇到下一个 `--` 就停止收集位置参数）：`--profile-use 剧情 --force` ✅，`--profile-use --force 剧情` ❌。

---

### 实测输出

#### 1. 列出：`--profile-list`

```bash
ntl-builder.exe --profile-list
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[配置档] 配置档目录：E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\profiles
[配置档]   docsdemo  文档演示档  启用 2 个 mod  更新 2026-09-26T12:32:33
[配置档] * ghostdemo  手写导入的档  启用 2 个 mod  更新 2026-09-26T12:32:34
[配置档] 共 2 个配置档（活动：ghostdemo）
```

一行一档，列 = `标记  id  显示名  启用 N 个 mod  更新时间`；`*` = 当前活动档。目录里一个档都没有时：

```
[配置档] 还没有配置档（用 --profile-create <id> 新建）
```

> ⚠ 这行提示里的 `--profile-create` **并不存在**（真开关是 `--profile-new`），属于待修的文案瑕疵。

#### 2. 看详情：`--profile-show [id]`

```bash
ntl-builder.exe --profile-show
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[配置档] id: ghostdemo
[配置档] 名称: 手写导入的档
[配置档] 说明: 演示导入 + 找不到 mod 的提示
[配置档] 创建: 2026-09-26T12:32:34  更新: 2026-09-26T12:32:34
[配置档] 章节: chapter1
[配置档] mods 根: mods
[配置档] 启用 mod：2 个
[配置档]   - demoa.docs
[配置档]   - nosuch.mod
[配置档] 设置：2 项
[配置档]   - debug_live = 1
[配置档]   - lang = "zh"
```

设置项按**键名**排序，值直接是 `config.json` 里的原始 JSON 片段（字符串带引号）。
省略 id 时显示**活动档**；活动档指针指向一个不存在的档时，这里会打印 `找不到配置档 vanished` 而**退出码仍是 0**。

#### 3. 新建：`--profile-new`

```bash
ntl-builder.exe --profile-new docsdemo --name "文档演示档" --desc "MANAGE.md 示例：把当前启用态存成一个档"
```

```
[配置档] 已创建 docsdemo（启用 2 个 mod）
[配置档] 已创建 docsdemo
```

两行：第一行来自模块，第二行来自 CLI 包装。`--from` 指定来源档（复制它的启用集合与设置）；来源档找不到时会**降级**成按当前实际状态生成：

```
[配置档] 找不到来源档 nosuch，改为按当前实际状态生成
```

id 已存在时不覆盖，但仍会打印「已创建」且退出码 0：

```bash
ntl-builder.exe --profile-new docsdemo
```

```
[配置档] 已存在同名档，未覆盖：docsdemo
[配置档] 已创建 docsdemo
```

> `--desc` 实测有效，但**用法行里没列它**：`用法: --profile-new <id> [--name 显示名] [--from 已有档id]`（这一行写在 **stderr**）。
> `--profile-new` 无参数时：stdout 只有 `游戏根: …`，stderr 是上面那行用法，退出码 **2**。

#### 4. 复制与改名：`--profile-copy` / `--profile-rename`

```bash
ntl-builder.exe --profile-copy docsdemo dcopy --name "复制出来的档"
ntl-builder.exe --profile-rename dcopy drenamed
```

```
[配置档] 已复制 docsdemo → dcopy
[配置档] 已重命名 dcopy → drenamed
```

目标已存在时不覆盖（退出码 **1**）：

```
[配置档] 目标已存在，未覆盖：docsdemo
```

改名 = 换文件名。实现是**先写新文件成功、再删旧文件**，中途失败最多多出一份档，不会丢数据；若改的正是活动档，指针会一起切到新 id。

#### 5. 应用（切换）：`--profile-use`

```bash
ntl-builder.exe --profile-use docsdemo
```

```
[配置档] demoa.docs: 启用
[配置档] demod.docs: 禁用
[配置档] 已写入 2 项设置
[配置档] 已应用 docsdemo：改动 2 个 mod，2 项设置
[配置档] 已应用 docsdemo（改动 2 个 mod）
```

* 每条 `启用 / 禁用` 都是一次 `mod.json` 的读-改-写，**只动 `enabled` 一个键**，其它字段（含未知字段）原样保留。
* **没被这档列出的 mod 会被禁用**（上面 `demod.docs` 就是这样被关掉的）。
* 档里有、但 `mods/` 里扫不到的 id 只警告，不中断：

```
[配置档] 忽略：找不到 mod nosuch.mod
[配置档] 已应用 ghostdemo：改动 1 个 mod，1 项设置
```

* **幂等**：紧接着再应用同一个档：

```bash
ntl-builder.exe --profile-use docsdemo
```

```
[配置档] 已应用 docsdemo：改动 0 个 mod，0 项设置
```

值已经一致的 mod 与设置键**一律不写盘**（连 `config.json` 的时间戳都不变），所以反复执行安全、也适合写进启动脚本。
档不存在时打印 `[配置档] 找不到配置档 nosuch`，退出码 **2**。

#### 6. 删除：`--profile-delete`

```bash
ntl-builder.exe --profile-delete docsalt          # 普通档，直接删
ntl-builder.exe --profile-delete docsdemo         # 当前活动档 → 拒绝
ntl-builder.exe --profile-delete docsdemo --force # 加 --force
```

```
[配置档] 已删除配置档 docsalt（mod 文件未动）
[配置档] docsdemo 是当前活动档，删除需 --force
[配置档] 已经没有配置档了，活动档指针已清空
[配置档] 已删除配置档 docsdemo（mod 文件未动）
```

* 只删 `profiles/<id>.json`，**绝不删 mod 文件**（日志里也会写明）。
* 两类档需要 `--force`：`default`（`[配置档] default 是默认档，删除需 --force`）与**当前活动档**。
* 删掉的若是活动档：还有别的档就自动切到列表里第一个（`[配置档] 活动档已切到 <id>`）；一个都不剩就把指针清空。
* 被拒绝或档不存在时退出码是 **1**（不是 2 —— 与 export/import 的约定不一致，见「已知限制」）。

#### 7. 导出与导入：`--profile-export` / `--profile-import`

```bash
ntl-builder.exe --profile-export docsdemo
```

```
[配置档] 已导出 docsdemo → E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\profiles\docsdemo.export.json
```

不写 `--out` 时落在 `profiles/<id>.export.json`；`--out` 可以是任意路径：

```bash
ntl-builder.exe --profile-export docsdemo --out "E:\aiwork\out\Neutraled2\_feat\docs-c1\docsdemo-backup.json"
```

```
[配置档] 已导出 docsdemo → E:\aiwork\out\Neutraled2\_feat\docs-c1\docsdemo-backup.json
```

导出文件是**自包含**的（带 schema 版本，便于以后升级格式）：

```json
{
  "schema": 1,
  "kind": "neutraled.profile",
  "api": "1.0.0",
  "exported": "2026-09-26T12:32:05",
  "profile": { "id": "docsdemo", "name": "文档演示档", "enabled": [ "demoa.docs", "demob.docs" ], "settings": { "lang": "zh" } }
}
```

导入：

```bash
ntl-builder.exe --profile-import "<…>\docsdemo.export.json"
```

```
[配置档] 已导入 docsdemo（启用 2 个 mod）
[配置档] 已导入 1 条
```

* 吃**带 `profile` 封装的导出文件**，也吃**裸的档对象**（手写一个 `.json` 直接导入即可）。
* 同名档已存在时拒绝并退出 **2**：`[配置档] docsdemo 已存在（加 --force 覆盖）`；加 `--force` 覆盖成功。
* 内容非法时：`[配置档] 导入失败：…` / `[配置档] 导入失败：内容里没有配置档`，退出码 2。

---

### 关键设计

#### 启用态的真相只有一处

配置档**不保存**「我改过哪些 mod.json」，而是把启用态视为 `mods/…/mod.json` 里 `enabled` 的当前值（**缺字段 = 启用**，`Mods.cs:94`）。因此：

* GUI 的勾选框、配置档切换、手工改 `mod.json` 改的都是同一处，不会互相覆盖成两份事实；
* `--profile-new` 不带 `--from` 时采集的是**当前实际启用态**（等价于「把现在的状态存成一个档」），不是凭空一个空档。

> ⚠ **mod id 会变成小写**：`mods/DemoA/docs/chapter1/` 这个 mod 的 id 是 `demoa.docs`（由 `name` + 作者目录名生成后规范化）。
> 档里的 `enabled` 列表写的就是这个运行时 id，手写档时要照抄小写形式。

#### 活动档指针（两处，一主一镜像）

| 位置 | 角色 |
| --- | --- |
| `config.json: active_profile` | **权威**。`--profile-use` 会写它 |
| `profiles/active.json` | **镜像**（`{"active":"<id>"}`），给外部工具/脚本读 |

读取顺序是 `config.json: active_profile` → `profiles/active.json: active` → `default`。所以：

* 外部工具只写 `active.json` 也能生效（实测：把 `config.json` 里的 `active_profile` 键删掉后，`--profile-list` 依然报告 `活动：ghostdemo`，`--profile-show` 也直接显示它，并打上 `*`）；
* 反过来，`config.json` 里的指针**可能悬空**（指向已删除的档）—— 实测这时 `--profile-list` 照常列出全部档、只报告 `活动：vanished`，`--profile-show` 打印 `找不到配置档 vanished`，**都不会崩**。

#### 消息语言跟着 `config.json` 的 `lang` 走

`--profile-use` 会把档里的 `settings.lang` 写进 `config.json`，而 CLI 的消息语言又读 `config.json`（`--lang` 可临时覆盖）—— 所以**切换一个 `lang=en` 的档之后，后续命令的输出会变成英文**。实测连续两条命令：

```
[Profiles] demoa.docs: enabled
[Profiles] demod.docs: disabled
[Profiles] Wrote 2 settings
[Profiles] Applied docsdemo: 2 mods changed, 2 settings changed
[Profile] Applied docsdemo (2 mods changed)

[配置档] 已应用 docsdemo：改动 0 个 mod，0 项设置
```

（第一段的英文不是缺翻译：那时 `config.json` 的 `lang` 是 `en`；同一档写回 `lang: "zh"` 之后的第二条命令就是中文。
注意**你自己起的档名、mod 名不会跟着变** —— 那是数据，不是界面文案。）

#### 名字与路径的安全校验

`id` 来自命令行、`mod.json`、导入文件或 Web 请求，所以统一在入口判定一次（`Profiles.cs:12-48 PathGuard`）：

* `IsSafeName`：非空、≤128 字符、不等于 `.`/`..`、不含 `..`、不含路径分隔符与通配符，只允许字母/数字（含中文等非 ASCII 字母）与 `_ - . @ +`；
* `Under`：归一化后做前缀比较（Windows 忽略大小写）—— **任何删除/覆盖动手前都要过这一关**，所以 `../evil` 这类 id 既进不了文件名，也删不到 `profiles/` 之外。

实测：`--profile-new "名字带空格"` 能创建（中文 id 合法）；`--profile-new ../evil` 与 `--profile-new bad/id2` 被拒绝。

---

### 已知限制

1. **`--profile-new` 恒返回 0**。id 非法（`../evil`）或同名已存在时，模块只打一行 `非法 id：…` / `已存在同名档，未覆盖：…`，CLI 仍然追加 `[配置档] 已创建 ../evil` 并退出 0 —— 脚本**不能靠退出码判断新建是否真的发生**，要自己查 `profiles/<id>.json` 是否存在。
2. **空列表的提示文案是错的**：`用 --profile-create <id> 新建` —— 没有 `--profile-create` 这个开关，正确的是 `--profile-new`。
3. **`--profile-delete` 被拒时退出 1，而 `--profile-export`/`--profile-import` 的参数/重名问题退出 2**，同一族命令的退出码语义不统一（`0` 成功、`1` 失败、`2` 用法/参数问题、`3` 被拒绝 是 MANAGE.md 的约定）。
4. **`--desc` 有效但不在用法行里**；用法行只列 `--name` 与 `--from`。
5. **档里找不到的 mod 只会被忽略**（一行 `[配置档] 忽略：找不到 mod <id>`）。这种档看起来「应用成功」，实际少改了几个 mod —— 手工编辑档、或 mod 被删掉之后容易踩到。
6. **配置档不记录 mod 的版本与内容**：它只管「开/关」。要能还原到某个版本，用快照（[MANAGE.md](MANAGE.md)）；要整游戏回退用恢复点（[MANAGE.md](MANAGE.md)）。
7. **采集的设置键是白名单**：只有上面那 8 个键 + 档里原本记过的键会被跟到；其它模块以后新加的键，除非该档曾经记过，否则不会自动采集。
8. **游戏内控制台没有配置档命令**。控制台里的 `profile` 是**性能统计**（FPS / 缓存命中 / Hook 数 / 对象数，`api/ntl_console_profile.gml`、`ntl_i18n_init.gml:118`）—— 与本文的配置档**同名但无关**。切档目前只有 CLI 与 GUI/Web 两个入口。
9. **未验证的部分**：GUI / Web / 插件侧的配置档入口（本轮只测 CLI）；游戏**正在运行时**切档（会改 `mod.json`，下一次部署才生效，未实测并发场景）；macOS/Linux 上的行为（本机 Windows 10 实测，路径校验与大小写规则在源码里按平台分支）。
10. **`profiles/active.json` 的语义与 [MANAGE.md](MANAGE.md) §1 的描述有出入**：那里写它「仅记录，不改行为」，实测它在 `config.json` 的 `active_profile` 缺失/为空时**会被当成活动档**（回退读取，见「活动档指针」）。

---

### 相关文件

| 位置 | 说明 |
| --- | --- |
| `builder/Profiles.cs` | 配置档读写 / 应用 / 导入导出（本文的实现） |
| `builder/CliFeatures.cs` | `--profile-*` 的接线、位置参数解析与退出码 |
| `builder/ConfigFile.cs` | `config.json` 的读-改-写（保留未知键，UTF-8 无 BOM）+ 镜像到存档区 |
| `builder/Paths.cs` | 游戏根探测与 `Neutraled/*` 各目录常量 |
| `docs/MANAGE.md` §2 | 配置档概览（速查） |
| `docs/MANAGE.md` §3 / §4 | 单 mod 版本快照 / 整游戏恢复点 |

---

## 快照（SNAPSHOTS）

> **一句话**：把**某一个 mod 的整个目录**按版本存一份**真实副本**（`snapshots/<modId>/<版本>/`），之后一条命令就能把这个 mod 原地切回那一版 —— 用来在升级/试验之后退回来。

配置档管「开哪些 mod」（见 [MANAGE.md](MANAGE.md)），整游戏回退用恢复点（见 [MANAGE.md](MANAGE.md)）；本文只管**单个 mod 的版本**。
功能概览见 [MANAGE.md](MANAGE.md) §3。

> **关于下面的输出**：本文每个代码块都是**真跑出来的**（exe 指向一个沙箱游戏根 `…\_feat\docs-c1\sb`，里面有 4 个演示 mod），避免动真实仓库。
> 在你机器上第一行是你的真实游戏根，其余格式一致。`[快照]` 行都在 **stdout**；失败原因写 **stderr** 并带 `[错误] ` 前缀。

---

### 目的

mod 升到新版之后想退回去，常见做法是「重装旧包」或「从备份里翻」——前者要知道下载地址，后者容易拷错目录。快照把这件事变成：

* **存**：`--snapshot-create <modId>` —— 把 `mods/` 里这个 mod 的目录整份复制进 `Neutraled\snapshots\<modId>\<版本>\`；
* **回**：`--snapshot-use <modId> <版本>` —— 用那份副本**原地覆盖**回来，多余文件一并清掉，**回切之前还会自动把现状另存一份**（后路）。
* **保底**：`--snapshot-auto` —— 给每个 mod 的当前版本各存一份（已存在则跳过），可以放心反复跑、适合放进部署流程。

实测：4 个文件的 mod 存一份不到 0.2 秒；回切 4 个文件 + 清理 1 个多余文件同样不到 0.2 秒。

---

### 快速上手

```bash
# 存一份（版本号缺省 = 读这个 mod 的 mod.json 的 version）
ntl-builder.exe --snapshot-create demoa.docs --note "升级 2.0 之前"

# 看这个 mod 有哪些版本
ntl-builder.exe --snapshot-list demoa.docs

# 切回去（先把当前内容另存成 auto-<时间戳>，再覆盖）
ntl-builder.exe --snapshot-use demoa.docs 1.0.0

# 给所有 mod 的当前版本存保底快照（幂等）
ntl-builder.exe --snapshot-auto
```

---

### 目录与文件

```
<游戏根>/Neutraled/
  snapshots/
    demoa.docs/                       ← 一个 mod 一个目录（用 mod id 命名）
      1.0.0/                          ← 一个版本一份完整副本
        .snapshot.json                ← 元数据（不参与内容指纹，也不被复制）
        mod.json                      ← 以下是这个 mod 目录的原样副本
        gml/main.gml
        data/notes.txt
        scripts/a.lua
      auto-20260926-123615/           ← 回切前自动留下的后路
  mods/DemoA/docs/chapter1/           ← live：真正生效的那份
```

> 快照按 **mod id**（不是目录名）归类。id 由 `mods/<name>/<作者>/<目标>/` 推导并规范化成小写（`DemoA` + `docs` → `demoa.docs`），比对时**忽略大小写**，所以 `--snapshot-create DemoA.docs` 与 `demoa.docs` 等价。

### .snapshot.json 字段

每份快照目录里都有一份元数据（实测内容）：

```json
{
  "modId": "demoa.docs",
  "version": "1.0.0",
  "name": "演示 Mod A",
  "author": "docs",
  "created": "2026-09-26T12:36:18",
  "source": "live",
  "sourceRef": "",
  "files": 4,
  "bytes": 262,
  "sha256": "3b9fe2169f47e50c818eba1fe2acc400b04e0ca9e0b892e89a5c987ce1810489",
  "note": "",
  "action": "auto"
}
```

| 字段 | 含义 |
| --- | --- |
| `modId` / `version` | 归属 mod 与版本号（= 目录名） |
| `name` / `author` | 取自被快照目录里的 `mod.json`；没有就退回 modId / 空 |
| `created` | 建立时间 `yyyy-MM-ddTHH:mm:ss`（元数据缺失时用目录创建时间兜底） |
| `source` | 来源：`live`（当前装着的）\| `dir`（导入的目录）\| `zip`（导入的压缩包）\| `gamebanana` |
| `sourceRef` | 导入时的原始路径 / 下载地址；`live` 为空 |
| `files` / `bytes` | 内容文件数与字节数（**不含** `.snapshot.json` 自己） |
| `sha256` | **内容树指纹**（见「内容指纹」） |
| `note` | `--note` 写进来的备注 |
| `action` | 这份快照怎么来的：`create`（手动）\| `auto`（保底）\| `import`（导入）\| `use`（回切前自动存的那份）\| `gamebanana`。**纯记录**，回切时不看它 |

> 键名是**驼峰混小写**（`modId` / `sourceRef` 驼峰，其余全小写）—— 这是 api 层与网页端直接读的键名，改名会让前端读不到值。

---

### 命令一览

| 命令 | 作用 | 实测退出码 |
| --- | --- | --- |
| `--snapshot-list [modId]` | 列全部快照；给 modId 就只列这个 mod | 0（没有也是 0） |
| `--snapshot-create <modId> [--version 版本] [--note 备注]` | 把当前 **live** 目录存成一份快照 | 0；非法 id / 找不到 mod / 非法版本号 **1**（消息在 stderr） |
| `--snapshot-use <modId> <版本> [--force]` | 原地回切（`--force` = 不先存 auto 后路） | 0；快照不存在 **2**；名字非法/越界 **1** |
| `--snapshot-import <modId> <目录或zip> [--version 版本]` | 从目录或压缩包导入一份快照 | 0；路径不存在 / 解压失败 **1** |
| `--snapshot-delete <modId> <版本> [--force]` | 删除一份快照（**不动 mods/**） | 0；只剩最后一份且没 `--force` **1** |
| `--snapshot-auto` | 给所有 mod（含禁用、含全部章节）的当前版本存保底快照；已有同版本则跳过 | 0 |

**位置参数必须写在选项之前**：解析器从主开关往后收集位置参数，**遇到第一个以 `--` 开头的参数就停**。实测把选项写在前面：

```bash
ntl-builder.exe --snapshot-create --version 9.9.9 demoa.docs
```

```
[stdout] 游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[stderr] 用法: --snapshot-create <modId> [--version 版本] [--note 备注]
[退出码] 2
```

写对是 `--snapshot-create demoa.docs --version 9.9.9`。选项本身**可以**放在位置参数之后（`--snapshot-use demoa.docs 1.0.0 --force` ✅），因为具名选项是从整个 argv 里找的。

---

### 实测输出

#### 1. 列出：`--snapshot-list`

一份都没有时：

```bash
ntl-builder.exe --snapshot-list
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[快照] 还没有快照
```

有条目时（第一行是汇总，之后一行一份）：

```
[快照] 共 3 个 mod，4 份快照
[快照] demoa.docs @ auto-20260926-123615  2026-09-26T12:36:15  live  4 个文件  288 B  回切 demoa.docs @ 1.0.0 前的自动快照
[快照] demoa.docs @ 1.0.0  2026-09-26T12:36:18  live  4 个文件  262 B  -
[快照] democ.docs @ 0.9.0  2026-09-26T12:36:18  live  1 个文件  90 B  -
[快照] demod.docs @ 3.0.0  2026-09-26T12:36:18  live  1 个文件  95 B  -
```

一行 = `modId @ 版本  时间  来源  N 个文件  体积  备注`（备注为空打 `-`）。
排序：mod id 升序 → 同一 mod 内**版本新→旧**（语义序，见「版本号怎么比」）→ 同版本按时间新→旧。
只列某个 mod：`--snapshot-list demoa.docs`（有快照时**不打汇总行**，直接列条目；没有快照时打 `[快照] mod demob.docs 还没有快照`）。

#### 2. 存一份：`--snapshot-create`

```bash
ntl-builder.exe --snapshot-create demoa.docs
```

```
[快照] 已保存 demoa.docs @ 1.0.0: 4 个文件, 262 B
[快照] 已创建 demoa.docs @ 1.0.0
```

版本号缺省 = 读这个 mod `mod.json` 的 `version`（读不到就用扫描结果，再兜底 `1.0.0`）。**同版本已存在时绝不覆盖**，而是自动加后缀：

```bash
ntl-builder.exe --snapshot-create demoa.docs           # 再来一次
```

```
[快照] demoa.docs @ 1.0.0 已存在，改存为 1.0.0-2
[快照] 已保存 demoa.docs @ 1.0.0-2: 4 个文件, 262 B
[快照] 已创建 demoa.docs @ 1.0.0-2
```

带版本号与备注：

```bash
ntl-builder.exe --snapshot-create demoa.docs --version 2.0.0 --note "第二个版本"
```

```
[快照] 已保存 demoa.docs @ 2.0.0: 4 个文件, 262 B
[快照] 已创建 demoa.docs @ 2.0.0
```

出错时**消息在 stderr**（stdout 只有 `游戏根:` 一行），退出码 1：

```bash
ntl-builder.exe --snapshot-create ../evil
ntl-builder.exe --snapshot-create nosuchmod
ntl-builder.exe --snapshot-create demoa.docs --version bad/ver
```

```
[错误] [快照] 非法 mod id: ../evil
[错误] [快照] 找不到 mod 目录: nosuchmod
[错误] [快照] 非法快照版本: bad/ver
```

（`../evil` 与 `bad/ver` 都是名字安全检查挡下来的：不允许 `..`、路径分隔符；只允许字母/数字（含中文）与 `_ - . @ +`。）

#### 3. 回切：`--snapshot-use`

先手工把 live 改乱（改一个文件、删一个文件、加一个 `extra.tmp`），再回切：

```bash
ntl-builder.exe --snapshot-use demoa.docs 1.0.0
```

```
[快照] 已保存 demoa.docs @ auto-20260926-123615: 4 个文件, 288 B
[快照] 已回切 demoa.docs @ 1.0.0: 恢复 4 个文件, 清理 1 个多余文件
[快照] 已切回 demoa.docs @ 1.0.0（4 个文件）
```

* 第一行是**后路**：回切前先把现状整份存成 `auto-<yyyyMMdd-HHmmss>`（`--force` 可跳过）。
* 第二行是回切本身：`恢复 N 个文件`、`清理 M 个多余文件`（live 里有、快照里没有的文件会被删掉，否则回切不干净），随后清掉空目录。
* 第三行来自 CLI 包装。

回切后目录实测回到快照内容（`main.gml` 恢复成原文、`notes.txt` 回来、`extra.tmp` 消失）：

```
mod.json  137 B
data\notes.txt  19 B
gml\main.gml  68 B
scripts\a.lua  38 B

// 演示 mod A 的主脚本
function demo_a_step() {
  return 1;
}
```

快照不存在时（退出码 **2**）：

```
[快照] 找不到快照: demoa.docs @ 9.9.9
```

> **指纹漂移警告**：如果快照目录里的内容被就地改动过（编辑器保存、安装器原地覆盖、手改），回切前会核对内容指纹并提示 —— **只警告不中止**，因为紧接着就会把现状另存成 auto 快照，用户仍有后路。实测（往快照里的 `gml\main.gml` 追加一行后再回切）：

```bash
ntl-builder.exe --snapshot-use demoa.docs 1.0.0 --force
```

```
[快照] 警告：demoa.docs @ 1.0.0 的内容与建立时不一致（可能被就地修改过），回切结果未必可靠
[快照] 已回切 demoa.docs @ 1.0.0: 恢复 4 个文件, 清理 0 个多余文件
[快照] 已切回 demoa.docs @ 1.0.0（4 个文件）
```

（`--force` = 跳过 auto 后路，所以这次没有第一行 auto 快照；这也是那条「后路还在」说法的例外——用了 `--force` 就没有后路。）

#### 4. 导入：`--snapshot-import`

从**目录**导入（来源 = `dir`，版本缺省读目录里 `mod.json` 的 `version`）：

```bash
ntl-builder.exe --snapshot-import demoa.docs "E:\aiwork\out\Neutraled2\_feat\docs-c1\snapimport"
```

```
[快照] 已保存 demoa.docs @ 1.5.0: 2 个文件, 151 B
[快照] 已导入 demoa.docs @ 1.5.0
```

从**压缩包**导入（zip / 7z / rar，走 SharpCompress；来源 = `zip`）：

```bash
ntl-builder.exe --snapshot-import demoa.docs "E:\aiwork\out\Neutraled2\_feat\docs-c1\DemoA-1.6.0.zip"
```

```
[快照] 已保存 demoa.docs @ DemoA-1.6.0: 2 个文件, 151 B
[快照] 已导入 demoa.docs @ DemoA-1.6.0
```

上面这个包**故意没写 `version`**，于是版本号按兜底顺序取到了**压缩包文件名** `DemoA-1.6.0`。完整兜底顺序：

| 输入 | 版本号顺序 |
| --- | --- |
| 目录 | 目录里 `mod.json` 的 `version` → 目录名 |
| 压缩包 | 解出来的 `mod.json` 的 `version` → 压缩包文件名（去扩展名） → `1.0.0` |

压缩包会先解到系统临时目录（`%TEMP%\ntl-snap-<8 位随机>`，不落在游戏目录里），复制完立刻删掉；包里如果套着一层「同名文件夹」，会自动下沉到真正的内容根（实测的包就是 `DemoA-1.6.0/…` 这种结构）。
包内路径一律做越界校验，命中就打 `[快照] 压缩包内含越界路径，已拒绝: {key}` 并整体失败（防 zip-slip）；含 `:` 的路径同样拒绝。实测（故意造了一个带 `../evil.txt` 的 zip）：

```bash
ntl-builder.exe --snapshot-import demoa.docs "…\evil.zip"
```

```
[stdout] 游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[stdout] [快照] 压缩包内含越界路径，已拒绝: ../evil.txt
[stderr] [错误] [快照] 解压失败: E:\aiwork\out\Neutraled2\_feat\docs-c1\evil.zip
[退出码] 1
```

注意这条的**前一行在 stdout**（拒绝原因由工具直接打印）、后一行在 **stderr**（异常统一加 `[错误]` 前缀）。

导入后的列表（`source` 列能看出每份快照的来源）：

```
[快照] demoa.docs @ DemoA-1.6.0  2026-09-26T12:35:43  zip  2 个文件  151 B  -
[快照] demoa.docs @ 2.0.0  2026-09-26T12:35:38  live  4 个文件  262 B  第二个版本
[快照] demoa.docs @ 1.5.0  2026-09-26T12:35:42  dir  2 个文件  151 B  -
[快照] demoa.docs @ 1.0.0-2  2026-09-26T12:35:37  live  4 个文件  262 B  -
[快照] demoa.docs @ 1.0.0  2026-09-26T12:35:36  live  4 个文件  262 B  -
```

#### 5. 删除：`--snapshot-delete`

```bash
ntl-builder.exe --snapshot-delete demoa.docs 2.0.0
```

```
[快照] 已删除 demoa.docs @ 2.0.0
```

**只剩最后一份**时会拦一下（退出码 1）：

```bash
ntl-builder.exe --snapshot-delete demob.docs 2.1.0
ntl-builder.exe --snapshot-delete demob.docs 2.1.0 --force
```

```
[快照] demob.docs @ 2.1.0 是该 mod 最后一份快照，加 --force 才会删除
[快照] 已删除 demob.docs @ 2.1.0
```

只删 `snapshots/<modId>/<版本>/`，**绝不动 mods/ 里的文件**；删完该 mod 一份快照都不剩时，顺手把空的 `snapshots/<modId>/` 目录也清掉（所以再次 list 会变成 `mod demob.docs 还没有快照`）。

#### 6. 保底：`--snapshot-auto`

```bash
ntl-builder.exe --snapshot-auto
```

```
[快照] 已保存 demoa.docs @ 1.0.0: 4 个文件, 262 B
[快照] 已保存 demob.docs @ 2.1.0: 2 个文件, 111 B
[快照] 已保存 democ.docs @ 0.9.0: 1 个文件, 90 B
[快照] 已保存 demod.docs @ 3.0.0: 1 个文件, 95 B
[快照] 已为 4 个 mod 建立保底快照
```

注意 `democ.docs` 在配置档里是**禁用**状态，照样会被快照（扫描时含禁用、含全部章节）。**幂等** —— 紧接着再跑一次：

```
[快照] demoa.docs @ 1.0.0 已存在，跳过
[快照] demob.docs @ 2.1.0 已存在，跳过
[快照] democ.docs @ 0.9.0 已存在，跳过
[快照] demod.docs @ 3.0.0 已存在，跳过
[快照] 已为 0 个 mod 建立保底快照
```

所以可以放心放进部署脚本（比如 `--deploy-all` 之前跑一遍）。

---

### 关键设计

#### 真实复制，绝不硬链接（重要）

快照的建立与回切**两个方向都用 `File.Copy`**，不省这块磁盘。原因是踩过一次：

> 早期版本建立快照时走 `Platform.LinkOrCopy`（硬链接省盘），快照与 `mods/<id>` 共享同一份数据（同一个 inode）。
> 于是「就地改一下 live 的 `gml\main.gml`」（编辑器保存、安装器解包覆盖、仓库里大量 `File.Copy(..., true)` 都是就地截断写法）
> **把快照里的那份也一起改了**，`--snapshot-use` 回切出来的还是被改过的内容 —— 快照等于白存。

实测证据（`fsutil hardlink list`，只列出自己 = 没有别的硬链接）：

```
# live
\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\mods\DemoA\docs\chapter1\gml\main.gml

# 快照里的同名文件
\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\snapshots\demoa.docs\1.0.0\gml\main.gml
```

回切覆盖时也是「**先删目标文件再复制**」（`File.Delete` 后 `File.Copy`），目的同样是**断掉可能存在的硬链接**，保证 live 拿到的是一份独立数据。
需要硬链接的场景（部署缓存之类）直接用 `Platform.TryHardLink`，别改回快照这边。

#### 内容指纹（`sha256`）

`TreeHash`：把目录里每个文件（**跳过 `.snapshot.json` 自己**）算成一行 `相对路径\t长度\t单文件SHA256`，按行序数排序、`\n` 拼接后再整体 SHA256 → 小写十六进制。

* 路径统一 `/` 并排序 ⇒ **同样的内容在任何机器上算出同一串**；
* 单文件读不了（被占用/没权限）不导致整份失败，那一行用 `err:<异常类型名>` 占位，仍能区分「这份和别的不一样」；
* 指纹用于**回切前的漂移检查**（见上）；元数据里 `files <= 0` 时，列表会现场统计目录内容兜底。

#### 回切是「补/盖 + 删多余 + 清空目录」

对快照里的每个文件：建目录 → 目标存在就先删 → `File.Copy`；然后遍历 live，**把快照里没有的文件删掉**；最后自底向上删空目录（只动这个 mod 目录之下）。
于是回切结果 = 快照内容的精确副本，既不会留下旧版的残留文件，也不会留下空目录。
删除失败（文件被占用）只打 `[快照] 删除失败: {路径}（{原因}）` 并继续，返回值是**实际恢复的文件数**。

#### 版本号怎么比

按 `. - _` 切段逐段比较：两边都是数字就按数字比（`1.10 > 1.9`），否则按序数字符串比（忽略大小写）；前缀完全相同则**段数多的算新**。
数字超出 64 位范围时退回字符串比较。这个规则决定列表里的排序，也决定「哪个版本更新」。

#### 内置的安全校验

| 位置 | 校验 |
| --- | --- |
| `modId` / 版本号 | `PathGuard.IsSafeName`：非空、≤128 字符、不等于 `.`/`..`、不含 `..`、不含路径分隔符/通配符/控制字符 |
| 写/删目标 | `PathGuard.Under`：归一化后必须位于 `snapshots/`（写、删）或 `mods/`（回切）之下 |
| 解压条目 | 逐条做越界校验（zip-slip）+ 拒绝含 `:` 的路径 |
| 索引/版本冲突 | 同版本已存在 → 自动 `-2` / `-3` …（`-999` 全占满才报 `版本号已被占满，无法分配`），**永不覆盖** |

#### 元数据坏了也能用

`.snapshot.json` 缺失或损坏时，列表**不会整份消失**：改用目录实际内容现算，并打印一行警告。实测（把一份元数据写坏、把另一份删掉之后再列）：

```bash
ntl-builder.exe --snapshot-list
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[快照] 元数据损坏，按目录内容兜底: …\snapshots\democ.docs\0.9.0\.snapshot.json（Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. Path: $.version | LineNumber: 0 | BytePositionInLine: 36.）
[快照] 缺少元数据文件，按目录内容兜底: …\snapshots\demod.docs\3.0.0
[快照] 共 3 个 mod，4 份快照
[快照] demoa.docs @ auto-20260926-123615  2026-09-26T12:36:15  live  4 个文件  288 B  回切 demoa.docs @ 1.0.0 前的自动快照
[快照] demoa.docs @ 1.0.0  2026-09-26T12:36:18  live  4 个文件  262 B  -
[快照] democ.docs @ 0.9.0  2026-09-26T12:36:18  live  1 个文件  90 B  -
[快照] demod.docs @ 3.0.0  2026-09-26T12:36:18  live  1 个文件  95 B  -
```

两条警告之后条目照常列出（`democ.docs` 与 `demod.docs` 都在），退出码仍是 0。

兜底出来的条目 `source` 记成 `live`、`action` 记成 `create`、时间取目录创建时间 —— 只要能列出来，就能照常回切。

---

### 已知限制

1. **没有 `--snapshot-show`**。`Snapshots.PrintShow`（逐行打印 mod/版本/名称/作者/时间/来源/文件/指纹/备注/路径）在 CLI 里**没有接线**，所以 `sha256` 只能自己去读 `snapshots/<modId>/<版本>/.snapshot.json`，或者等它接上。
2. **快照是整目录真实复制，占盘是 1:1**。同一个 mod 存 N 个版本就是 N 份完整数据（**不用硬链接**是有意为之，理由见上）。
   [MANAGE.md](MANAGE.md) §3 提到的「67 个 mod 约 2.7 GB」是它当时的实测；本机沙箱里 4 个 mod / 4 份快照只有几百字节。
   大 mod（真实 mod 动辄几十 MB）存多份前先想一下磁盘。
3. **大目录复制慢且不显示进度**：`--snapshot-create` 只有开始/结束两行输出，几万文件的 mod 会静默跑一段时间。
4. **非数字版本号排序会显得怪**：版本号按「切段 + 两边都是数字才按数字比」排序，所以 `DemoA-1.6.0` 会排在 `2.0.0` **前面**（首段 `DemoA` 按字符串 > `2`）。想让排序符合直觉，版本号就用纯数字点分（`1.0.0` / `2.0.0`）。
5. **`--snapshot-use` 不检查游戏是否在运行**（恢复点 `--restore-apply` 才检查）。游戏跑着时回切 mod 目录，正在运行的进程可能仍持有已删除的文件句柄；回切只影响**下一次部署**的输入，不会热改当前进程。
6. **回切只认 mods 目录**：目标目录必须落在 `Neutraled\mods\` 之下（`[快照] 拒绝操作 mods 之外的目录`），所以打包目录（`.ntlmod` 解包出来的位置）里的 mod 无法用快照回切。
7. **失败消息在 stderr 且退出码不统一**：`create` / `import` / 名字非法是 **1**（stderr 带 `[错误] ` 前缀），而 `--snapshot-use` 找不到快照是 **2**（stdout 一行 `找不到快照: …`）。脚本要同时看退出码和 stderr。
8. **`--snapshot-auto` 只认扫描到的 mod 列表**：扫描器跳过的目录（不是 `root`/`chapterN` 叶子、`mod.json` 坏掉等）不会进快照，也不会被提示。
   另外它按「mod id 去重」——同一个 mod 的多个章节目录只会存一份（快照的是该 mod 的**一个**目录，多章节 mod 的其它章节不在这份快照里）。
9. **未验证的部分**：GUI / Web / 插件侧的快照入口（本轮只测 CLI）；7z/rar 压缩包导入（实测只用了 zip，代码走同一个 SharpCompress 入口）；正在运行的游戏进程并发读写 mod 目录；macOS/Linux（本机 Windows 10 实测，路径比较在源码里按平台分支）。

---

### 相关文件

| 位置 | 说明 |
| --- | --- |
| `builder/Snapshots.cs` | 快照的建立 / 回切 / 导入 / 删除 / 指纹（本文的实现） |
| `builder/CliFeatures.cs` | `--snapshot-*` 接线、位置参数解析与退出码 |
| `builder/Platform.cs` | `LinkOrCopy` / `TryHardLink`（快照**故意不用**硬链接） |
| `builder/Mods.cs` | mod 扫描（决定 `--snapshot-auto` 覆盖哪些 mod、版本号从哪来） |
| `docs/MANAGE.md` §3 | 快照概览（速查） |
| `docs/MANAGE.md` §2 / §4 | 配置档 / 整游戏恢复点 |

---

## 恢复点（RESTORE）与黑名单（BLACKLIST）

> **一句话**：**恢复点**把「各章节 `data.win` + `config.json` + 那一刻启用的 mod 列表」整份存下来，一条命令就能把整个游戏退回那个时间点；**黑名单**是另一件事 —— 它只在浏览 GameBanana 时把你不想要的条目过滤掉。

概览见 [MANAGE.md](MANAGE.md) §4–§5；单 mod 的版本回退见 [MANAGE.md](MANAGE.md)；「哪些 mod 开着」的组合切换见 [MANAGE.md](MANAGE.md)。

> **关于下面的输出**：本文的命令与输出都是**真跑出来的**，用的是沙箱假游戏根 `E:\aiwork\out\Neutraled2\_feat\docs-c1\sb`（里面有 4 个演示 mod、1 份配置档，以及我自己写的 3 个占位 `data.win`：root 4096 B / chapter1 5120 B / chapter2 6144 B —— 只为验证复制、打包与校验链路，真实游戏里它们是数百 MB 的产物）。
> 在你机器上第一行会是你的真实游戏根，其余格式一致。
> **一处例外**：`--restore-apply` 本轮**没有执行**（任务约束禁止运行它），它的行为只按源码描述并明确标注 —— 见「7. 回切」小节。

---

### 目的：什么时候需要它

* 装了一个整包 mod（汉化 / DOJO / 60fps 这类会换掉 `data.win` 的包），玩过一轮后想回到「什么都没装」的状态；
* 手改过 `data.win`、试过别人的产物，出了问题又说不清改了什么；
* 想记住「通关前那一刻」的游戏状态，以后还能回到同一套配置。

恢复点回答的是「**整个游戏**怎么回去」；如果只是想把某一个 mod 退回旧版本，用 [MANAGE.md](MANAGE.md) 更快也更省盘。

### 恢复点里有什么

```
<游戏根>/Neutraled/restore/
  rp-20260926-123841/
    manifest.json          ← 清单：章节、mod 列表、config.json 快照、每个文件的 SHA256
    data/root/data.win     ← 游戏根那一份
    data/chapter1/data.win ← 各章节那一份
    data/chapter2/data.win
```

实测（沙箱里建了带数据的点之后的目录）：

```
rp-20260926-123841\manifest.json  1164
rp-20260926-123841\data\root\data.win  4096
rp-20260926-123841\data\chapter1\data.win  5120
rp-20260926-123841\data\chapter2\data.win  6144
rp-20260926-123843\manifest.json  515          ← --no-backup：只有清单，没有 data/
```

`manifest.json` 字段（真实内容见下）：`id / name / created / from / profileId / chapters[] / mods[] / bytes / hasData / gameVersion / schema / files[] / config{}`；
每个 `files[]` 项是 `chapter / path / sha256 / bytes / linked`（`linked` 是早期硬链接方案的遗留字段，现在恒为 `false`，见「关键设计」）。

一次真实建立产出的 `manifest.json`（`config` 段就是当时 `config.json` 的原样快照）：

```json
{
  "id": "rp-20260926-123841",
  "name": "演示恢复点",
  "created": "2026-09-26 12:38:42",
  "from": "live",
  "profileId": "",
  "chapters": [ "root", "chapter1", "chapter2" ],
  "mods": [ "demoa.docs" ],
  "bytes": 15360,
  "hasData": true,
  "gameVersion": "exe-0-639259937947727053",
  "schema": 1,
  "files": [
    { "chapter": "root",     "path": "data/root/data.win",     "sha256": "AD7FACB2586FC6E966C004D7D1D16B024F5805FF7CB47C7A85DABD8B48892CA7", "bytes": 4096, "linked": false },
    { "chapter": "chapter1", "path": "data/chapter1/data.win", "sha256": "A11937F356A9B0BA592C82F5290BAC8016CB33A3F9BC68D3490147C158EBB10D", "bytes": 5120, "linked": false },
    { "chapter": "chapter2", "path": "data/chapter2/data.win", "sha256": "FD9243E1BA57263ED469C3BDBD7ADE6EC5254E7ED924A9F5737FA44749933CC0", "bytes": 6144, "linked": false }
  ],
  "config": {
    "_comment": "沙箱演示用的最小配置",
    "lang": "zh",
    "theme": "dark",
    "auto_chapter": 4,
    "debug_live": 1
  }
}
```

> `gameVersion` 直接复用启动缓存的游戏版本指纹（读 `backup` 里的**原版** `data.win`，不会因为部署改写顶层 `data.win` 而变化）。沙箱里没有 `backup`，所以上面是兜底值；真实安装里它是一串哈希。

### 快速上手

```bash
# 1) 现在建一个恢复点（默认连各章节 data.win 一起真实复制，会占盘）
ntl-builder.exe --restore-create --name "装汉化前"

# 2) 看有哪些点
ntl-builder.exe --restore-list

# 3) 玩坏之后回退（先关掉游戏；不加 --force 时会自动把「现在」也存成一个点）
ntl-builder.exe --restore-apply rp-20260926-123841
```

### 命令一览

| 命令 | 作用 | 实测退出码 |
| --- | --- | --- |
| `--restore-list` | 列出全部恢复点（新→旧） | 0 |
| `--restore-create [--name 名称] [--from live\|profile] [--profile 档id] [--no-backup]` | 建立恢复点；**没有位置参数** | 0 |
| `--restore-apply <id> [--force]` | 回切（游戏在跑时拒绝） | **本轮未执行**；源码契约：0 成功 / 1 id 非法 / 2 点不存在或没有数据副本 / 3 游戏正在运行 |
| `--restore-export <id> <输出.ntlrestore>` | 导出成单个 zip 包 | 0（**失败也是 0**，见「已知限制」1） |
| `--restore-import <文件.ntlrestore> [--force]` | 导入包 | 0；找不到包 / 包非法 **1** |
| `--restore-delete <id> [--force]` | 删除一个恢复点（`--force` = 删不掉时先清只读属性再删） | 0；找不到 **1** |

**位置参数必须写在选项之前**：`--restore-apply rp-1 --force` ✅；`--restore-create` 没有位置参数，`--name` / `--from` / `--profile` / `--no-backup` 都是具名选项，顺序随意。

---

### 实测输出

#### 1. 列出：`--restore-list`

```bash
ntl-builder.exe --restore-list
```

一个点都没有时：

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点] 目录：E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\restore
[恢复点] 没有恢复点
```

有几个点时（排序是 `created` 新→旧；一行一个点、字段名固定，方便 grep）：

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点] 目录：E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\restore
[恢复点] rp-20260926-123843  name=只存清单  created=2026-09-26 12:38:43  from=live  hasData=false  chapters=root,chapter1,chapter2  mods=1  bytes=0
[恢复点] rp-20260926-123841  name=演示恢复点  created=2026-09-26 12:38:42  from=live  hasData=true  chapters=root,chapter1,chapter2  mods=1  bytes=15360
```

`mods` 是**个数**（不是列表）；`hasData=false` 就是「建立时用了 `--no-backup`，没有数据可回切」。

#### 2. 建立：`--restore-create`

```bash
ntl-builder.exe --restore-create --name "演示恢复点"
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点]   root → 复制
[恢复点]   chapter1 → 复制
[恢复点]   chapter2 → 复制
[恢复点] 已建立数据副本：3 个章节，15360 字节
[恢复点] 已创建 rp-20260926-123841（演示恢复点）
```

id 自动生成 `rp-yyyyMMdd-HHmmss`（同一秒内重复建立会加 `-2` / `-3`，**绝不覆盖**已有的点）；不写 `--name` 时名称 = id。

只存清单（知道当时的配置、但没有数据可回切）：

```bash
ntl-builder.exe --restore-create --no-backup --name "只存清单"
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
  [扫描] mods 清单命中磁盘缓存（4 个）
[恢复点] 只写了清单（没有备份数据），回切时无法还原 data.win
[恢复点] 已创建 rp-20260926-123843（只存清单）
```

#### 3. 记一套配置档的启用表：`--from profile --profile <档id>`

```bash
ntl-builder.exe --restore-create --from profile --profile docsdemo --name "按配置档记录"
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点]   root → 复制
[恢复点]   chapter1 → 复制
[恢复点]   chapter2 → 复制
[恢复点] 已建立数据副本：3 个章节，15360 字节
[恢复点] 已创建 rp-20260926-123949（按配置档记录）
```

四种写法的 `from` / `profileId` / `mods` 实测对比：

| 命令 | `from` | `profileId` | `mods` |
| --- | --- | --- | --- |
| `--restore-create`（默认） | `live` | `(空)` | `[demoa.docs]` ← 当前实际启用态 |
| `--restore-create --from profile --profile docsdemo` | `profile` | `docsdemo` | `[demoa.docs, demob.docs]` ← **该档记录的启用表** |
| `--restore-create --from profile`（不写 --profile） | `profile` | `(空)` | `[demoa.docs]` ← 没有档 id，退化成当前实际态 |
| `--restore-create --profile ../evil` | `live` | `(空)` | `[demoa.docs]` ← 非法 id 被拒绝（`[恢复点] 拒绝：非法配置档 id ../evil`），不影响建立 |

`from` 只认 `profile`（大小写不敏感），其它值一律当 `live`。

#### 4. 导出与导入：`--restore-export` / `--restore-import`

```bash
ntl-builder.exe --restore-export rp-20260926-123841 "E:\backup\before-boss.ntlrestore"
```

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点] 冻结副本（硬链接可能被就地改写）→ 打包含 manifest.json
[恢复点] 打包 4 个文件 → E:\aiwork\out\Neutraled2\_feat\docs-c1\demo.ntlrestore
[恢复点] 已导出 → E:\aiwork\out\Neutraled2\_feat\docs-c1\demo.ntlrestore
```

`.ntlrestore` 就是一个 zip（Deflate），条目名 = 相对恢复点目录的路径：

```
manifest.json  1164  560
data/root/data.win  4096  20
data/chapter1/data.win  5120  21
data/chapter2/data.win  6144  22
```

（沙箱里 3 个 `data.win` 是零填充，所以压缩后只有 20 字节；真实 `data.win` 压缩收益有限，含 6 章的包仍是数百 MB 级。）

导入：

```bash
ntl-builder.exe --restore-import "E:\backup\before-boss.ntlrestore"
```

同名点已存在时**不覆盖**：

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点] 已存在同名恢复点 rp-20260926-123841，用 --force 覆盖
[恢复点] 已导入 rp-20260926-123841
```

加 `--force` 覆盖（旧点先被挪到 `.rp-…old-<时间戳>` 暂存，成功后才删）：

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点] 已覆盖恢复点 rp-20260926-123841
[恢复点] 解包 4 个文件 → E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\restore\rp-20260926-123841
[恢复点] 已导入 rp-20260926-123841
```

包不存在：

```
[恢复点] 找不到恢复包 E:\aiwork\out\Neutraled2\_feat\docs-c1\nosuch.ntlrestore   （退出码 1）
```

导入时会先解到 `restore/.import-<进程号>` 临时目录校验 `manifest.json`，再整体挪进 `restore/<id>/`；条目路径越界（zip-slip）会打 `[恢复点] 拒绝：压缩包条目路径越界 <条目>` 并整体失败。

#### 5. 删除：`--restore-delete`

```bash
ntl-builder.exe --restore-delete rp-20260926-123843
ntl-builder.exe --restore-delete nosuchpoint
```

```
[恢复点] 已删除 rp-20260926-123843            （退出码 0）
[恢复点] 找不到恢复点 nosuchpoint               （退出码 1）
```

删除只作用于 `Neutraled/restore/` 下那一个目录。

#### 6. 手抄 / 解压出来的恢复点：没有 `manifest.json` 也能用

从别人那里拷来的、或手动解包的目录，只要里面有 `data.win`，列表里就能看到它（兜底：章节名取 `data.win` 所在目录名，时间取目录修改时间）：

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[恢复点] [警告] manifest.json 损坏（brokenrp）：Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. Path: $.files[0] | LineNumber: 0 | BytePositionInLine: 30.
[恢复点] 目录：E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\restore
[恢复点] brokenrp  name=brokenrp  created=2026-09-26 12:39:09  from=  hasData=false  chapters=  mods=0  bytes=0
[恢复点] handmade  name=handmade  created=2026-09-26 12:39:09  from=  hasData=true  chapters=root  mods=0  bytes=4096
[恢复点] rp-20260926-123843  name=只存清单  created=2026-09-26 12:38:43  from=live  hasData=false  chapters=root,chapter1,chapter2  mods=1  bytes=0
[恢复点] rp-20260926-123841  name=演示恢复点  created=2026-09-26 12:38:42  from=live  hasData=true  chapters=root,chapter1,chapter2  mods=1  bytes=15360
```

* `brokenrp` = `manifest.json` 损坏（我自己写坏的）→ 一行警告 + 兜底（`from=` 空、`chapters=` 空）；
* `handmade` = 只有 `data\root\data.win`、完全没有 `manifest.json` → 兜底后 `hasData=true`、`chapters=root`；
* 这两种情况**都不会从列表里消失**，`--restore-export` 也会替它们补写一份 `manifest.json`（注意：这会往你的目录里写文件）。

#### 7. 回切：`--restore-apply`（⚠ 本轮没有执行）

按任务约束，`--restore-apply` **没有被真跑过**，所以这里没有可粘贴的真实输出。以下是源码契约（`RestorePoints.cs:176-251`、`CliFeatures.cs:229-238`），真机验收待补：

1. id 非法 → `[恢复点] 拒绝：非法恢复点 id …`，退出码 **1**；
2. `DELTARUNE` 进程在跑 → `[恢复点] 拒绝：游戏正在运行（DELTARUNE），请先关闭游戏再恢复`，退出码 **3**；
3. 点不存在 / 没有数据副本 → `[恢复点] 找不到恢复点 …` 或 `… 没有数据副本（建立时用了 --no-backup），无法恢复`，退出码 **2**；
4. 不加 `--force` 时先自动存一个 `auto-<时间戳>` 恢复点：`[恢复点] 恢复前已自动备份当前状态 → <id>`；
5. 逐个文件回切（每个章节一行 `[恢复点]   chapter1 → 复制`），做法是**先写临时文件 `<目标>.ntl-apply` 再同目录原子改名**，绝不硬链接；
6. 写回 `config.json` 快照（`[恢复点] 已写回 config.json 快照`，没有快照则 `… 没有配置快照，跳过 config.json`）；
7. 还原 mod 启用态（`[恢复点] <modId>: 启用` / `…: 禁用`，找不到的 mod 只打 `忽略：找不到 mod <id>`）；
8. 收尾 `[恢复点] 已应用 <id>（改动 N 个文件）` + CLI 行 `[恢复点] 已恢复 <id>（N 个文件）`，退出码 0。

> 注意第 8 步的 `N` 不只是 `data.win` 的个数：**它把写回的 `config.json` 与被改动的每个 `mod.json` 都算进去**（源码里 `changed` 依次累加）。

---

### 黑名单（真开关是 `--block-*`，不是 `--blacklist-*`）

黑名单**与恢复点无关**：它只在 GameBanana 的搜索/浏览/下载入口过滤条目（`Blacklist.FilterOut` / `IsBlocked`）。它**不会**禁用、隐藏或删除你本地已经装好的 mod。

```bash
ntl-builder.exe --block-add 12345 --kind id --note "不想再看到这个"
ntl-builder.exe --block-add "Boss Rush" --kind name
ntl-builder.exe --block-list
ntl-builder.exe --block-remove 12345
```

实测：

```
游戏根: E:\aiwork\out\Neutraled2\_feat\docs-c1\sb
[黑名单] 文件：E:\aiwork\out\Neutraled2\_feat\docs-c1\sb\Neutraled\blacklist.json
[黑名单] 没有条目                                                        ← 空表

[黑名单] 已加入 12345
[黑名单] 已加入 Boss Rush
[黑名单] 已存在 12345                                                     ← 重复加入：不写第二遍
[黑名单] 已加入 12345                                                     ← CLI 仍然报「已加入」（退出码 0）
[黑名单] 未知 kind：bogus（可用 id/name/category）                        ← kind 写错
[黑名单] 已加入 x                                                        ← CLI 仍然报「已加入」（退出码 0），**实际什么都没写**

[黑名单] 共 2 条                                                         ← 上面那条 bogus 没进去
[黑名单] kind=id  value=12345  note=不想再看到这个  added=2026-09-26 12:39:18
[黑名单] kind=name  value=Boss Rush  note=名字里带这个就别显示  added=2026-09-26 12:39:19

[黑名单] 已移除 12345                                                    （退出码 0）
[黑名单] 没找到 nosuchvalue                                              （退出码 1）
[黑名单] 未知 kind：bogus（可用 id/name/category）                        （退出码 1）
```

落盘的文件（`Neutraled/blacklist.json`）：

```json
{
  "entries": [
    {
      "kind": "name",
      "value": "Boss Rush",
      "note": "名字里带这个就别显示",
      "added": "2026-09-26 12:39:19"
    }
  ]
}
```

| kind | 比较对象 | 规则 |
| --- | --- | --- |
| `id` | GameBanana 条目的数字 id | **精确**比较（大小写敏感） |
| `name` | 条目名 | **大小写不敏感的子串** |
| `category` | 条目分类（Model） | **大小写不敏感的子串** |

* `--kind` 省略时默认 `id`；
* 条目值不能为空、不能超过 200 字符、不能含控制字符（否则 `[黑名单] 拒绝：非法条目 …`）；
* 文件不存在或坏掉都当空表（`[黑名单] 文件损坏，已忽略：…`），绝不会挡住搜索。

---

### 关键设计

#### 数据副本是**真实复制**，不是硬链接（踩过坑）

早期版本为了省盘，用硬链接（`Platform.LinkOrCopy`）存各章节 `data.win`。实测翻车，已改回真实复制（`RestorePoints.cs:64-75`、`:464-475` 的注释记录了完整经过）：

* 硬链接副本与游戏里的 `data.win` **共享同一份数据**，而仓库里有一批 `File.Copy(..., true)` 是**就地截断**写法 —— 任何外部就地写都会把恢复点里的副本一起改掉；
* 复现：建立恢复点后就地改花 `chapter1_windows\data.win`（4096 B `2D69F59F…` → `D349A508…`）→ 恢复点里的副本同步变成 `D349A508…`（`fsutil hardlink list` 显示三条路径共享同一 inode），manifest 里记的 SHA256 立刻成为陈旧值；随后回切只打一行 SHA256 警告却仍按「成功」退出，游戏文件还是改花的；
* 代价是盘：本机 6 个 `data.win` 合计约 **530 MB**，每建一个带数据的恢复点就多占这么多。只想「记配置、不占盘」就用 `--no-backup`（但这种点无法回切）。

**回切**同样不用硬链接，而是「临时文件 + 同目录原子改名」覆盖：既不会把游戏文件和恢复点重新绑在一起，也不会在「先删后建」中途失败时把游戏文件弄丢。

#### 三道防线

1. **路径守卫**（`PathGuard`）：恢复点 id、manifest 里的 `files[].path`、压缩包条目名全部先归一化再做前缀校验（必须在 `restore/` 或该点目录之下），`..` 与绝对路径直接拒绝；
2. **游戏运行检查**：回切前查 `DELTARUNE` 进程，在跑就拒绝（退出码 3）—— 否则会覆盖正在被读的文件；
3. **SHA256 校验**：回切前逐个比对副本与建立时的哈希，不一致打 `[恢复点] [警告] 副本与建立时不一致（SHA256 校验失败）：<相对路径>` —— 只警告、不中止（旧版本建出来的点可能仍是硬链接，用户至少能在「恢复成功」之前看到不对劲）。

#### 章节白名单是硬编码的

探测顺序固定为 `root` + `chapter1`…`chapter5`（`RestorePoints.cs:84`）。这个游戏目前正好六份 `data.win`，所以够用；但将来若出现 `chapter6`，它不会被备份也不会被还原（见「已知限制」4）。

---

### 已知限制

1. **`--restore-export` 失败也返回 0**，并且会打印一行路径为空的 `[恢复点] 已导出 → `。实测（点不存在）：

   ```
   [恢复点] 找不到恢复点 nosuchpoint
   [恢复点] 已导出 → 
   （退出码 0）
   ```

   脚本不能靠退出码判断导出是否成功，要自己检查目标文件是否存在。
2. **`--restore-import` 遇到同名点且没给 `--force`** 时，只打 `已存在同名恢复点 … 用 --force 覆盖`，但 CLI 仍然追加 `[恢复点] 已导入 <id>` 并退出 **0** —— 实际上**什么都没导入**（返回的是磁盘上已有的那份）。要判断是否真导入，看有没有 `已覆盖` / `解包` 行。
3. **`--restore-apply` 本轮未真跑**（见「7. 回切」），是本文唯一没有真实输出支撑的命令；退出码 1/2/3 与 auto 后路均来自源码，真机验收待补。
4. **章节白名单只到 `chapter5`**（`RestorePoints.cs:84`）。
5. **建点很占盘**：真实复制 6 个 `data.win` ≈ 530 MB/点（`--no-backup` 可建「只有清单」的点，但那种点不能回切）。
6. **恢复点不含 mod 文件本身**，也不含 `profiles/*.json`：只有 `config.json` 快照 + 启用 id 列表。mod 被删掉之后，回切只能打一行 `忽略：找不到 mod <id>`。
7. **副本被就地改动只会警告**：SHA256 不一致时照样按现有内容恢复 —— 「恢复成功」不等于「内容与建立时一致」，要看清那行 `[警告]`。
8. **手抄目录会被写入**：`--restore-export` 会替没有 `manifest.json` 的恢复点补写一份（写进该点目录）；`--restore-import` 也会按磁盘实际内容重写导入点的 `manifest.json`。
9. **黑名单不拦已装的 mod**，只在 GameBanana 搜索/浏览/下载入口过滤；`--blacklist-*` 这个开关**不存在**（真开关是 `--block-list` / `--block-add` / `--block-remove`）。
10. **黑名单的 `name` / `category` 是子串匹配**，容易误伤：`--block-add Rush --kind name` 会把任何名字里含 `Rush` 的条目一起挡掉。
11. **`--block-add` 恒退出 0**：kind 写错或值非法时只打一行警告，CLI 仍报 `[黑名单] 已加入 <值>`（实际没写）；重复加入同理（`已存在 …` + `已加入 …`）。
12. **未验证的部分**：GUI / Web / 插件的恢复点入口；退出码 3（游戏正在运行）的真机复现；游戏运行中的并发行为；macOS/Linux（本机 Windows 10 实测）。

---

### 相关文件

| 位置 | 说明 |
| --- | --- |
| `builder/RestorePoints.cs` | 恢复点的建立 / 回切 / 导出 / 导入 / 删除（本文的实现） |
| `builder/Blacklist.cs` | 黑名单条目与 GameBanana 过滤 |
| `builder/CliFeatures.cs` | `--restore-*` / `--block-*` 的接线、位置参数解析与退出码 |
| `builder/Paths.cs` | 游戏根探测、`Neutraled/restore` 与各章节 `data.win` 路径 |
| `builder/ConfigFile.cs` | `config.json` 的读-改-写（恢复点快照写回也走它） |
| `builder/Cache.cs` | `GameVersion`（恢复点里 `gameVersion` 的来源） |
| `docs/MANAGE.md` §4–§5 | 恢复点与黑名单概览（速查） |
| `docs/MANAGE.md` §3 / §2 | 单 mod 版本快照 / 配置档 |
