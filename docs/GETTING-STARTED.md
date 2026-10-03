# 入门：安装与第一个 mod（GETTING-STARTED）

> **本篇包含**（按顺序）：
> - [Neutraled 安装与卸载](#neutraled-安装与卸载) — 下载、安装、卸载与目录结构
> - [30 分钟做出你的第一个 mod](#30-分钟做出你的第一个-mod) — 30 分钟从零做出第一个可玩 mod

## Neutraled 安装与卸载

### 设计原则

1. **不改动任何原有文件** —— 安装只在游戏目录新增 `Neutraled/` 文件夹
2. **可完整还原** —— 安装时把原版 `data.win` 备份到 `Neutraled/backup/`，卸载时恢复
3. **不碰存档** —— 官方存档在 `%LOCALAPPDATA%\DELTARUNE`，安装/卸载都不动
4. **Steam 兼容** —— 安装后游戏仍由 Steam 启动 → **计时 / 云存档 / 成就正常**

### 安装

```bash
# 自动检测游戏目录（Steam 注册表 + 库文件夹）
ntl-builder.exe --install

# 或手动指定
ntl-builder.exe --install --game "E:\steam\steamapps\common\DELTARUNE"
```

安装器会：
1. **自动找游戏目录**（读 `libraryfolders.vdf` + 注册表 `Steam App 1671210`）
2. **备份原版** `data.win`（顶层 + 各章节）→ `Neutraled/backup/`（**已存在则不覆盖**）
3. **复制运行所需内容**：`api/` `live/` `docs/` `lang/` `fonts/` `tools/` `templates/` `kristal/` `kristal-maps/` `sdk/` `plugins/` `web/` `scripts/` `bside/`（有 `mods/` 也一起带）
4. **把安装器自己复制到** `Neutraled/bin/`（发布包布局没有 `bin/`，否则装完游戏目录里没有工具）
5. **写安装标记** `Neutraled/installed.json`

### 发布包布局（install/ + src/）

官方发布包只有两个目录，解压后**保持同级**：

```
Neutraled-1.0.7/
  README.txt
  install/ntl-builder.exe           ← 安装器（单文件自包含，不需要另装 .NET）
  install/Magick.Native-Q8-x64.dll  ← 图像处理原生库，必须与 exe 同目录
  src/                              ← 源码 + 运行期载荷（api/ docs/ lang/ fonts/ tools/ …）
```

安装器会自动认**同级的 `src/`**（也认 `install/src/`，或显式 `--src <目录>`）：

```bash
install\ntl-builder.exe --install                        # 自动检测游戏目录
install\ntl-builder.exe --install --game "…\DELTARUNE"    # 手动指定
```

装完就是普通的开发布局：`<游戏>\Neutraled\` 里有全部载荷，工具在 `<游戏>\Neutraled\bin\ntl-builder.exe`。
想从源码自己编译：在 `src/builder/` 下 `dotnet build -c Release`（需要 .NET 9 SDK）；
打发布包本身用 `src/tools/package.ps1` —— **只支持 `-Flavor full`**：`-Flavor trim` 自 2026-09-28 起被**明确禁用**（`PublishTrimmed` 会剪掉 `System.Text.Json` 的反射序列化 ⇒ `mod.json` 读不进来 ⇒ **静默少 mod**，`--conflicts` 的退出码会从 2 变 0 而看不出问题）；打包脚本遇到 `-Flavor trim` 直接以**退出码 2 拒绝**。

### 卸载 / 还原

```bash
ntl-builder.exe --uninstall
```

卸载器会：
1. 从 `Neutraled/backup/` 恢复所有 `data.win`
2. 删除部署产物（`chapters.json` / `hook-registry.json` / `api-registry.json`）
3. 删除缓存与转换产物（`cache/` / `kristal-maps/`）
4. **保留** `Neutraled/` 目录本体、你的 `mods/` 与存档（如需彻底删除请手动移除）

### 安装后的使用

```
Neutraled/
  bin/ntl-builder.exe     ← 命令行（发布包装完在这；开发布局是 builder/bin/Release/net9.0/）
  ntl-gui.exe             ← 图形界面（只有源码树 / 自己编译时才有）
  api/  live/  docs/  lang/  fonts/  tools/  templates/  kristal/  kristal-maps/  sdk/
  mods/                   ← 你的 mod（发布包只带几个示例）
  backup/                 ← 原版 data.win 备份
  cache/                  ← 部署缓存（可设上限）
```

**启动游戏**（`bin/` 下，或网页界面）：
- **命令行**：`bin\ntl-builder.exe --deploy-all` → 自动查缓存 → 应用 → **由 Steam 拉起**
- **网页界面**：`bin\ntl-builder.exe --web`（浏览器里选章节 / 管理 mod / 点「部署并启动」）
- **有图形界面时**：GUI 里点「部署并启动」
- 也可以直接点 Steam 的「开始游戏」（用上次准备好的 data.win）

### ⚠️ 注意事项

- 安装/卸载前请**关闭游戏**
- 卸载**不会**删除你的 mod（在 `Neutraled/mods/`）与存档
- 如果用了目录联接（`--link-saves`），卸载不会自动解除，需手动删除联接
- `backup/` 里的原版文件**只写一次**，之后升级 Neutraled 不会覆盖它（保护你的原版快照）

---

## 30 分钟做出你的第一个 mod

> 面向**零基础**：不需要懂 GML、不需要懂游戏引擎，跟着做就能在游戏里看到自己的 mod。

---

### 第 0 步：认识三个词（1 分钟）

| 词 | 意思 |
|---|---|
| **Neutraled (ntl)** | mod 管理器。它把你的 mod「叠」到游戏上，**不改动游戏原文件** |
| **mod** | 你做的东西。一个文件夹，里面有 `mod.json`（说明）+ 脚本 |
| **Lua** | 脚本语言。**你只需要写这个**，不用碰游戏代码 |

---

### 第 1 步：生成 mod 骨架（2 分钟）

打开 PowerShell，粘贴：

```powershell
cd E:\steam\steamapps\common\DELTARUNE\Neutraled
.\builder\bin\Release\net9.0\ntl-builder.exe --new-mod 我的第一个Mod --author 你的名字
```

看到这个就成功了：

```
===== mod 骨架已生成 =====
  目录: ...\Neutraled\mods\我的第一个Mod\你的名字\chapter4
```

> **提示**：名字用中文没问题。作者的文件夹名也是你的署名。

---

### 第 2 步：看看生成了什么（3 分钟）

进入 `Neutraled\mods\我的第一个Mod\你的名字\chapter4\`，你会看到：

```
mod.json          说明文件（已经填好了）
main.lua          ★ 入口脚本 —— 从这里开始改
on_frame.lua      每帧执行的脚本
hooks/            拦截游戏函数的示例
README.md         说明
```

用**记事本**或 **VS Code** 打开 `main.lua`：

```lua
print("[我的第一个Mod] 你好，Neutraled！")
print("  Kristal 兼容层: " .. type(Kristal))
print("  我的 mod 正在运行！")
```

**`print` 就是「写一行日志」** —— 这是你调试的主要工具。

---

### 第 3 步：改一行，跑起来（5 分钟）

把 `main.lua` 最后一行改成：

```lua
print("  这是我的第一行改动！")
```

**保存文件**，然后**关闭游戏**（重要！），运行：

```powershell
cd E:\steam\steamapps\common\DELTARUNE\Neutraled
.\launch.ps1
```

游戏启动后，打开日志看结果：

```powershell
Get-Content "$env:LOCALAPPDATA\DELTARUNE\Neutraled\dr-api.log" | Select-String "我的第一个Mod"
```

应该看到：

```
[lua] [我的第一个Mod] 你好，Neutraled！
[lua]   这是我的第一行改动！      ← 你的改动生效了
```

🎉 **恭喜，你的第一个 mod 跑起来了！**

> **为什么不用重新部署？** 因为 Lua 脚本是**运行时读取**的 —— 改完重启游戏就生效。
> 这就是 Neutraled 的「零部署热重载」：调 mod 的速度快 20 倍以上。

---

### 第 4 步：在游戏里看到你的东西（7 分钟）

光看日志不过瘾？让我们在**游戏画面上**显示文字。

#### 4.1 新建一个 `on_draw.lua`

在 mod 目录里新建文件 `on_draw.lua`：

```lua
-- 每帧绘制时执行（画在游戏画面上）
draw_set_color(c_red)          -- 设置颜色（红色）
draw_text(20, 20, "我的 mod 在画画！")   -- 在 (20,20) 位置写文字
draw_set_color(c_white)        -- 恢复白色
```

#### 4.2 在 mod.json 里注册这个事件

打开 `mod.json`，找到 `"hooks": []` 这一行，**改成**：

```json
  "hooks": [],
  "scripts": {
    "on_draw": "on_draw.lua"
  }
```

> **注意**：`"hooks": []` 后面的逗号不能少。

#### 4.3 重启游戏

```powershell
Get-Process DELTARUNE -EA 0 | Stop-Process -Force
.\launch.ps1
```

进游戏后，**左上角会出现红色的「我的 mod 在画画！」** 🎨

---

### 第 5 步：改游戏行为（8 分钟）

现在做点更厉害的：**让游戏播音效时打印日志**。

#### 5.1 新建 `hooks/snd.lua`

```lua
-- args[1] 是声音资源 ID
print("[我的mod] 游戏正在播放音效: " .. tostring(args[1]))
return nil    -- nil = 不拦截，继续执行原函数
```

#### 5.2 在 mod.json 声明要拦截的函数

把 `"hooks": []` 改成：

```json
  "hooks": [
    { "script": "snd_play", "mode": "pre", "handler": "hooks/snd.lua" }
  ],
```

#### 5.3 重启游戏，进第 4 章

日志里会不断出现：

```
[lua] [我的mod] 游戏正在播放音效: 419
[lua] [我的mod] 游戏正在播放音效: 420
```

#### 三种拦截模式

| mode | 作用 | 返回值 |
|---|---|---|
| **`pre`** | 在原函数**之前**执行 | 返回非 nil → **跳过**原函数 |
| **`post`** | 在原函数**之后**执行 | 返回非 nil → **替换**返回值 |
| **`override`** | **完全接管** | 原函数不执行 |

> ⚠️ **多个 mod 冲突时，用 `pre`/`post` 可以共存** —— 这是 Neutraled 的核心优势。
> `override` 只有一个能生效。

---

### 第 6 步：用控制台调试（4 分钟）

游戏里按 **F2** 打开控制台：

```
> help           显示所有命令
> mods           看加载了哪些 mod
> version        看 Neutraled 版本
> clear          清屏
```

**控制台是你在游戏里唯一的调试窗口** —— 出问题时按 F2 看有没有线索。

---

### 常见问题

#### ❓ 改了脚本没效果

**99% 的原因是没重启游戏。** Lua 脚本在游戏启动时读取。

```powershell
Get-Process DELTARUNE -EA 0 | Stop-Process -Force
.\launch.ps1
```

#### ❓ 部署失败 / 改动没生效

**游戏还开着。** `data.win` 被占用时写盘会**静默失败**（显示成功但文件没变）。
`launch.ps1` 会自动帮你关游戏。

#### ❓ 日志里报错了

日志会给出**中文说明 + 修改建议**，例如：

```
[lua] [main.lua] 调用了不存在的函数
        Lua:  attempt to call a nil value (global 'myfunc')
        建议: 检查函数名拼写；或用 pcall 包住调用
```

#### ❓ 游戏崩了

不用担心存档 —— Neutraled 每次部署前**自动快照存档**。

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --save-list        # 看所有快照
.\builder\bin\Release\net9.0\ntl-builder.exe --restore-save      # 恢复到最近一次
```

而且 mod 的 Lua 错误**不会崩溃游戏**（已被隔离），只会在日志里报告。

#### ❓ 怎么知道自己写得对不对

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --lint
```

它会检查文件名、变量、语法等常见错误。

---

### 下一步学什么

| 想做什么 | 看哪里 |
|---|---|
| 完整的 Lua 语法 | `Neutraled/docs/SCRIPTING.md` |
| 修改游戏地图/章节 | `Neutraled/docs/PROJECT.md` 的「地图运行时」 |
| 用 Kristal mod 的资源 | `Neutraled/docs/SCRIPTING.md` 第 8 章 |
| 看别人的 mod 怎么写的 | `Neutraled/mods/` 里的示例 |
| 部署/缓存机制 | `Neutraled/docs/PIPELINE.md` |

---

### 速查表

```lua
-- 日志
print("文字")

-- 绘制（在 on_draw 事件里）
draw_set_color(c_red)              -- 颜色：c_red c_blue c_green c_white c_black ...
draw_text(x, y, "文字")
draw_rectangle(x1, y1, x2, y2, false)
draw_circle(x, y, r, false)

-- 存档 flag
Kristal.getFlag("名字")            -- 读
Kristal.setFlag("名字", 1)         -- 写

-- 声音
Kristal.playSound("snd_xxx")

-- 加载其他文件
local mylib = require("lib.mylib")

-- 判断与循环
if x > 10 then print("大") end
for i = 1, 10 do print(i) end
while x < 100 do x = x + 1 end
```
