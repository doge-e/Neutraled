# Neutraled (ntl)

> DELTARUNE 的 **Everest 式 mod 管理器 / API** —— 让所有 mod 平等叠加，**永不修改原版文件**。

```
                    ┌──────────────┐
   任意 mod  ──────▶ │  Neutraled   │ ──────▶  改动后的 data.win（可一键还原）
   （原生/传统/Kristal）│  注入 + 叠加  │
                    └──────────────┘
```

## 三条不可动摇的原则

1. **所有 mod 平等** —— 没有“主 mod”，没有特权
2. **不碰原版文件** —— 原版 `data.win` 备份到 `Neutraled/backup/`，卸载即完整还原
3. **保留 Steam 计时 / 云存档 / 成就** —— 启动始终走 Steam（`steam://rungameid/1671210`）

## 快速开始

```powershell
# 安装（自动检测 Steam 库；也可 --game "<游戏目录>" 手动指定）
ntl-gui.exe --install

# 部署（务必 root + 章节都做，否则游戏停在章节选择器跑的是旧代码）
ntl-gui.exe --deploy --chapter root
ntl-gui.exe --deploy --chapter chapter4

# 启动后游戏内按 F2 打开控制台：help / mods / hooks / profile / lang zh
# 游戏内「设置」菜单第 6 行多出「Mod 设置」（与官方项同款，打开后是原生二级菜单页）：章节 / 已加载模组 / 界面语言 / 重新部署并重启
ntl-gui.exe --uninstall      # 完整还原
```

## 导入传统 mod（自动转成 Neutraled 格式）

不需要管它是哪种生态，一条命令自动识别：

```powershell
ntl-gui.exe --import-mod <包目录 | zip | xdelta | data.win> [--dry-run]
```

支持 **Modding.xml / Deltamod(meta.toml) / mod_config.json / 裸 xdelta / 裸 data.win / Kristal / 原生 mod**。
详见 **[docs/PIPELINE.md](docs/PIPELINE.md)**。

## 共存规则（一句话）

> **一章只能有一个整包 data.win 基底；其余一切（Lua 脚本、hooks、GML patch、资源包、files 覆盖）都能任意叠加。**

多个 mod 抢基底时 Neutraled 会**响亮报告**（并写进 `conflicts.json`），不会静默丢弃：

```
[2/5] [警告] 4 个 mod 提供整包 data.win，本章只能用 1 个基底：
        ✔ 生效  converted.deltarune_60_fps.badartadventure
        ✘ 被忽略  converted.dojo.xanzo1        ← 它的脚本/资源/files 覆盖仍会生效
        → 想换基底：--base-mod <id>
```

## 能力一览

| 能力 | 说明 |
|---|---|
| 核心注入 | 引导 prepend + 控制器 + 事件总线（root 与各章都注入） |
| **函数 Hook** | pre / post / override；**多个 mod 可 hook 同一函数并共存** |
| **Lua 解释器** | 词法/语法/求值/元表/OOP/require/标准库 + **性能优化 ↓37–41%** |
| **mod 间联动** | 导出/导入符号、事件总线、共享总线、共享资源、独立存档（17/17 验收） |
| **Tiled 地图** | 转换 122/122 全部成功，运行时加载/绘制/碰撞/相机/玩家 |
| **Kristal 兼容层** | 类系统（22 个引擎基类）、对象/敌人、LOVE2D 桥接 |
| **传统 mod 导入** | 8 个真实 GameBanana mod 实测（见 docs/PIPELINE.md） |
| **一条龙下载转换** | `--install-id <gamebananaId>` / `--install-url <url>` 下载 + 自动探测包型并路由导入（xdelta / data.win / .ntlmod / Kristal / GM3P / 外部引擎）；旧名 `--fetch` / `--fetch-mod` 是可用别名 |
| 部署缓存 | 硬链接复用 21s → 0.8s，签名含 api 指纹自动失效 |
| 质量工具 | `--lint` / `--doctor` / `--smoke` / `--api-doc` / `--selftest` |
| 控制台 | **64 条命令** + 滚动/历史/宏/别名；输出中英双语（界面语言 zh/en 内置，另随包 4 种外部语言包） |
| **配置档 / 快照 / 恢复点** | `--profile-*`（启用集合 + `base_mod`；旧档里的游戏设置会继续套用）、`--snapshot-*`（每 mod 版本快照，真实复制）、`--restore-*`（整游戏恢复点，跨机导出/导入） |
| **GameBanana + 下载队列** | `--search` / `--gb-files` / `--install-id`（下载→识别→导入一条龙；旧名 `--gb-search` / `--gb-install` 是别名）、`--route-probe`（只读探测包型）、`--queue-*`（断点续传、黑名单） |
| **插件系统** | C# DLL 插件 + `plugin.json` 清单/权限，`AssemblyLoadContext` 可卸载加载（见 `sdk/`） |
| **主题** | 4 个内置主题 + 外部主题 JSON，`--theme-use` 直接改游戏内控制台配色 |
| **外部语言包** | `Neutraled/lang/lang_*.json`；内置 zh/en（100%），随包外部 4 种（de/es/fr/ja）当前覆盖率 69.2%（游戏内 82.3%），`--lang-coverage` 体检 |
| **游戏内 Mod 设置面板** | 设置菜单第 6 行（部署补丁注入，样式与官方项一致；原版 Return to Title / Back 顺移到第 7、8 行）：章节开关 / 已加载模组数 / 界面语言（6 种，即时切换；zh/en 完整，外部 4 种翻译进行中）/ 重新部署并重启 / mod 自注册项；打开后走游戏原生二级菜单页（`global.submenu == 51`），`↑↓` 选择、`Z`/`Enter` 确认、`X` 返回；字号用官方菜单字体（`mainbig`），菜单框保持原版尺寸、多加的行靠滚动条容纳（见 docs/MANAGE.md §11、§11.4、docs/MODMENU-API.md） |
| **内建字体补全** | 部署时按产物真正会用到的文本，自动把内置像素字体包的字形补进 `fnt_main` / `fnt_mainbig` / `fnt_small` / `fnt_legend`（只补真的缺的）；原版基底 + 中文 lang 包也不会再出现「整段文字空白」；译文本体由人工本地化组提供（见 docs/MANAGE.md §11.2、`--font-probe`） |
| **Web UI** | 零依赖 HttpListener 静态页（`--web`，跨平台；WinForms GUI 仍是 Windows 原生前端） |
| GUI / Studio | `ntl-gui.exe` 图形管理器、`ntl-gui.exe studio` 代码编辑器 |
| **地图 / 战斗 / 剧情编辑器** | `mapedit/`（`ntl-gui.exe mapedit`）：直接打开原版 data 编辑房间/图层/实例/瓦片；原版对话改文（文本覆盖）、战斗与剧情编辑；保存即生成 Neutraled mod；左侧房间按剧情流程排序（支线缩进挂主线下） |

> 术语：**mod** 与 **模组** 同义（本文档两种写法混用，指同一个对象）；**部署** = 由 builder 生成各章节 `data.win` 并切换产物，**安装** = 把 Neutraled 装进游戏目录、或把 mod 包导入 `mods/` —— 两者不是一回事。
## 文档

| 文档 | 内容 |
|---|---|
| [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md) | **安装 / 卸载 / 30 分钟写第一个 mod** |
| [docs/CONSOLE.md](docs/CONSOLE.md) | 控制台：命令系统 + 交互（滚动/历史/补全）+ 强力指令 |
| [docs/MANAGE.md](docs/MANAGE.md) | 管理与扩展：配置档 / 单 mod 快照 / 整游戏恢复点 |
| [docs/THEMES-LANGS.md](docs/THEMES-LANGS.md) | 主题（配色）与多语言（中英双语 + 外部语言包） |
| [docs/SCRIPTING.md](docs/SCRIPTING.md) | 脚本与联动：Lua 开发指南 / Live 脚本 / 跨 mod 联动 API |
| [docs/PIPELINE.md](docs/PIPELINE.md) | 转换与部署管线：传统 mod 导入 / 共存规则 / 字体 / 缓存 / 性能 |
| [docs/PROJECT.md](docs/PROJECT.md) | 项目状态 + 测试指南（lint/doctor/smoke 各自抓什么） |
| [docs/API_REFERENCE.md](docs/API_REFERENCE.md) | 自动生成的 API 文档 |
| [docs/CHAPTER_DEV.md](docs/CHAPTER_DEV.md) | 从零做一个自己的章节 |
| [docs/ROOM_PATCHES.md](docs/ROOM_PATCHES.md) | 房间/地图差分：mod.json 的 `roomPatches` / `rooms` 全字段、改名 vs 摘除、验证清单 |
| [docs/GB.md](docs/GB.md) | GameBanana 浏览、下载队列、黑名单 |
| [docs/PLUGINS.md](docs/PLUGINS.md) | 插件开发（SDK、清单、权限、钩子） |
| [docs/WEBUI.md](docs/WEBUI.md) | Web 界面（接口、端口、自动停止） |
| [mapedit/README.md](mapedit/README.md) | 编辑器（mapedit/）：房间编辑 / 原版文本改写 / 战斗与剧情 / 启动方式 / 测试 |
| [docs/UPDATE.md](docs/UPDATE.md) | 游戏更新：检测 / 提示 / 采纳新基线（Steam 更新后不静默降级） |
| [docs/PORTABILITY.md](docs/PORTABILITY.md) | 换机 / 可移植性：新机器安装、自定位守候、DOTNET_ROOT、测试脚本路径 |

## 从源码构建

```powershell
# 需要 .NET 9 SDK；另需 UTMT 依赖库（UndertaleModLib.dll / Underanalyzer.dll / Newtonsoft.Json.dll /
# Magick.NET-Q8-AnyCPU.dll / Magick.NET.Core.dll）：默认读 <游戏根>\UTMT\，可用环境变量 NTL_UTMT_DIR 指到别处
# （缺依赖时构建会报「找不到 UTMT 依赖库」，见 docs/MANAGE.md）
# 唯一入口 ntl-gui.exe：builder CLI / 地图编辑器 / Studio 都已合并成它的库
# （1.1.0 起不再产出 ntl-builder.exe / ntl-mapedit.exe / ntl-studio.exe）
dotnet build gui\Neutraled.Gui.csproj -c Release   # → gui\bin\Release\net9.0-windows\ntl-gui.exe
# 运行：ntl-gui.exe（管理器） / ntl-gui.exe <builder 开关> / ntl-gui.exe mapedit / ntl-gui.exe studio
# 用户目录装的 SDK（%USERPROFILE%\.dotnet）：直接跑框架依赖的 apphost 前要设 DOTNET_ROOT，
# 否则报 "Failed to resolve hostfxr.dll"。发布包是 self-contained，无此要求。

# 一键测试（静态套件；不启动游戏）
test.bat                # = _test/fulltest-static.ps1：构建/lint/doctor/smoke/部署/验证/打包/安装往返
test.bat -SkipPackage   # 跳过打包与安装往返
test.bat -OutDir <目录>  # 结果目录（默认 E:\aiwork\out\Neutraled2\fulltest；E: 不存在时退到 %USERPROFILE%\Desktop\aiwork\out\...）
# 真机 / 外部章节 / 安装器套件：_test/fulltest-game.ps1、_test/fulltest-external.ps1、tools/verify-install.ps1
```

## 已知限制

- **Kristal 主引擎**（`src/kristal.lua` 状态机）未移植 —— 当前是兼容层；Kristal 项目的 Lua 逻辑需手工迁移（`--import-kristal` 会打印待迁移清单，资源已自动转换）
- **Kristal 插件型 mod**（只有 scripts/+assets/，无 mod.json）无法独立转换，必须挂在 Kristal 项目下
- Lua 未实现：coroutine 部分语义 / `goto` / 完整 `string.format`
- 解释器有执行预算（约 1 万次循环内安全）；重活建议用 GML
- 深度重编译 mod（dojo/LM）与汉化存在索引映射缺口
- **整包 data.win 型 mod 每章只能启用一个**（见 docs/PIPELINE.md 的共存规则）
- **游戏内控制台没有配置档/快照命令**：`profile` 是性能统计；这些功能走 CLI（`--profile-*` 等）与 Web 界面（能套用）；
  GUI 里只有【工具箱 → 配置档（只读）】：只显示有哪些档、各开了几个 mod（`--profile-list --json` 的同一份数据），不提供套用
- 控制台的**历史行不随语言切换重排**（已打印的行保持当时的语言，标题/提示/后续输出才是新语言）
- `--game <根>` 在功能开关（`--profile-*` 等）上同样生效；沙箱测试请务必带上，否则会写进真实游戏根

## 环境

| 项 | 值 |
|---|---|
| 游戏 | DELTARUNE（GameMaker 2023.6），Steam App **1671210** |
| 日志 | `%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log` |
| 自动测试 | `_test\autotest.ps1`（自动切英文输入法 + 合成按键 + 截图） |
