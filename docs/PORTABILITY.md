# 换机 / 可移植性（PORTABILITY）

> 2026-11-08 在一台全新 Windows 机器上实测：没有 E: 盘、没有系统级 .NET、Steam 在
> `C:\Program Files (x86)\Steam\steamapps\common\DELTARUNE`。本篇记录「换机器/重装系统后
> 哪些东西本来会坏、现在怎么修好的」，以及新机验证清单。

---

## 1. 安装：只补缺，不覆盖玩家数据

`ntl-gui.exe --install` 会自动检测 Steam 库（也可 `--game "<游戏目录>"` 指定）。从 1.1.0 起：

- `mods/` 采用**只补缺**（目标已存在同名文件就跳过）：发布包里的 ExampleMod 不会覆盖玩家自己改过的 mod；
- 复制失败不再静默吞掉：`CopyDir` 把 IOException / UnauthorizedAccessException 收集起来，安装末尾打印
  「有 N 个文件复制失败」+ 前 8 条；单个文件（如 `console-theme.json`）也会被复制；
- 快捷方式的**工作目录 = 游戏根**（旧实现误用 exe 所在目录）；
- 安装器会顺手刷新守候启动器与已存在的开机项，指向**装好的** `<游戏根>\Neutraled\bin\ntl-gui.exe`。

## 2. 守候进程：VBS 自定位，不再写死盘符

`Neutraled\scripts\watch-external.vbs` 由 `--watch-autostart on` 生成。旧版把
`ntl = "...\ntl-gui.exe"` 写成绝对路径；发布包在别人机器上安装后，登录启动项里的路径不存在，
守候**静默退出**，表现就是「选外部章节没反应」。

现在 VBS 用自身位置反推：

```vb
here = fso.GetParentFolderName(WScript.ScriptFullName)   ' ...\Neutraled\scripts
root = fso.GetParentFolderName(here)                     ' ...\Neutraled
game = fso.GetParentFolderName(root)                     ' ...\DELTARUNE
ntl  = root & "\bin\ntl-gui.exe"
```

同一份脚本换机、换安装目录都能用；安装器也会重写它。验证：`Get-Process ntl-gui` 里应有一个
命令行含 `--watch-external --game <游戏根>`、且 `MainWindowHandle = 0`（静默无窗口）。

## 3. 更新检测：不再被 mtime / 键名大小写 / 章节叶子骗

`builder/GameUpdate.cs`：

| 旧问题 | 现在 |
| --- | --- |
| exe 指纹 = `长度@mtimeTicks`；Steam 校验或复制只改 mtime，内容没变也误报「游戏更新」并阻断部署 | `ExeFp` = `FileFp(DELTARUNE.exe)`（SHA256 前 16 位 + 长度）；`ExeFpMatches` 兼容旧的 `长度@ticks`（只比长度），升级后第一次不会误拦 |
| Steam `appmanifest_1671210.acf` 的键名大小写不统一（实测 `lastupdated` 全小写），旧正则只认 PascalCase → 「更新于 ?」 | `AcfVal` 正则加 `RegexOptions.IgnoreCase` |
| 整包基底按扫描顺序取第一个匹配，常常取到 `chapter1` 叶子的 `ref/data.win`；而 `deploy-state` 只记录 `root` 基底 → `BaseModChanged` 永远为真 | `ResolveBaseModFile` 先收集全部匹配，**优先 `root` 叶子**，没有再退回第一个 |

结论文案也改成「原版数据 / Steam 元数据 / 可执行文件有变化」，避免只变了 exe 时误导。

## 4. 外部章节：同一个 exe 只出现一次

`builder/Chapters.cs` 在生成章表后按 **exe 全路径**去重：同一份 Frostveil 既被
`mods/frostveil/external/chapter1/mod.json` 的 `kristal_external` 声明、又通过
`--add-external` 手动登记时，章节选择器不再出现两个同名条目；保留带 `Mods` 的那份。

## 5. 开发构建：用户目录 SDK 与 DOTNET_ROOT

没有系统级 .NET 时，可把 SDK 装进用户目录：

```powershell
$dst = "$env:USERPROFILE\Desktop\dotnet-install.ps1"
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $dst
& powershell -File $dst -Channel 9.0 -Quality GA -InstallDir "$env:USERPROFILE\.dotnet" -NoPath
```

仓库脚本会依次找 `C:\Program Files\dotnet\dotnet.exe` → `%USERPROFILE%\.dotnet\dotnet.exe` → `dotnet`。

⚠ 构建出的 `gui\bin\Release\net9.0-windows\ntl-gui.exe` 是**框架依赖** apphost；直接运行前要：

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
```

否则报 `Failed to resolve hostfxr.dll [not found]`。发布包里的单文件 exe 是 self-contained，不需要。
`_test/fulltest-*.ps1` 已自动设置该回退。

## 6. 测试脚本：路径全部可移植

- `_test/fulltest-static.ps1` / `fulltest-game.ps1` / `fulltest-external.ps1`：
  - 仓库根 `$N = Split-Path $PSScriptRoot -Parent`；游戏根 `$G = Split-Path $N -Parent`；
  - 可用 `DELTARUNE_DIR` 覆盖游戏根；
  - 输出目录：`E:\aiwork\out` 存在则沿用，否则 `%USERPROFILE%\Desktop\aiwork\out`；
  - 外部章节 exe 可用 `FROSTVEIL_EXE` 指定，否则找 E: / 桌面 `aiwork\frostveil`；
  - 启动游戏统一 `steam://rungameid/1671210`（不再假设 `E:\DELTARUNE.url` 存在）。
- `tools/package.ps1` / `tools/verify-install.ps1` / `tools/verify-all.ps1` / `test.ps1` /
  `scripts/rebuild.ps1` / `scripts/build-mapedit.ps1` / `sdk/build-samples.ps1` 同样不再写死盘符。
- 含中文的 `.ps1` 必须是 **UTF-8 带 BOM**（PowerShell 5.1 会把无 BOM 的 UTF-8 当 ANSI，中文注释导致
  解析错位）；纯 ASCII 脚本保持无 BOM。

## 7. 新机验证清单（实测记录见下）

```powershell
ntl-gui.exe --install                      # 自动检测 Steam 库
ntl-gui.exe --update-check                 # 0 = 没有更新
ntl-gui.exe --selftest                     # 8/8（会经 Steam 启动一次游戏）
ntl-gui.exe --lint                         # 0 错误
ntl-gui.exe --headless selftest            # 14/14
ntl-gui.exe mapedit                        # 打开编辑器（原版 data 只读基线）
ntl-gui.exe mapedit mapedit-test <ch1 data.win> <work>
ntl-gui.exe mapedit battle-test
ntl-gui.exe mapedit settings-test <work>
_test\fulltest-static.ps1                 # 静态：构建/部署/打包/安装往返
_test\fulltest-game.ps1                   # 真机：逐章启动/截图/日志
_test\fulltest-external.ps1               # 外部章节：守候/选择/回程
```

2026-11-08 新机结果：

| 项 | 结果 |
| --- | --- |
| 发布包沙箱安装 / 卸载 | 通过；data.win 逐字节还原；预置 mod 保留 |
| `--update-check` | 退出码 0；Steam buildid 24484059；基底取 `root/ref/data.win` |
| `--selftest` | 8/8（含经 Steam 启动、日志 0 错误） |
| GUI / 编辑器 | 管理器与 `mapedit` 均可启动；编辑器 147 房间、原版基线只读 |
| 编辑器 CLI | mapedit-test PASS；battle-test 135/0；settings-test PASS |
| 外部章节（冰封帷幕） | 守候拉起、窗口隐藏；关闭后 3 秒内回程；章节选择器只有一条外部条目 |
| `fulltest-static` | **60 PASS / 0 FAIL**（构建 / lint / 部署 6 槽 / 逐章 verify / 打包 / 沙箱安装往返 / 编辑器四套） |
| `fulltest-game` | **7 PASS / 0 FAIL**（root + chapter1..5 真机启动截图与日志断言 / console autorun 电池） |
| `fulltest-external` | **6 PASS / 0 FAIL**（守候静默 / 控制台注入 / 外部章节选择 16.1s / F2 / 输入隔离 / 关闭回程 2.1s） |

三套合计 **73 PASS / 0 FAIL**（2026-11-08 17:41，全新机器实测）。旧机器上 external 的 3 项失败根因是测试只写了
游戏根的一份 `config.json`，被存档区同名文件按 GameMaker 文件沙箱遮蔽；现在测试同时写两份，产品侧无需改动。
