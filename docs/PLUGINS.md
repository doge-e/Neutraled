# C# DLL 插件（PLUGINS）

> **一句话：** 让你用 C# 类库（`.dll`）挂在 builder 的部署 / 导入流程前后插一脚 —— 统计、改文件、甚至**否决**这次操作 —— 而完全不用改 Neutraled 自己的源码。

本文是插件系统的**深潜**版：清单字段、权限、接口、钩子、AssemblyLoadContext 加载模型、CLI、SDK、排查表。
只想看"命令怎么敲"的话，`docs/MANAGE.md` 第 7 节是简版。

> ⚠ **先看这一条**：当前构建里**任何插件都会被登记成 `broken` 并且不会被加载**（含自带的 `sample-hello`）。
> 照本文敲命令会看到 `损坏 1` / `[跳过] ... 清单损坏（）`。这是真实缺陷，根因与影响见文末
> **「已知限制」第 1 条** —— 本文照实记录观测到的行为，不写成"应该怎样"。

---

## 1. 目录布局

```
<游戏根>/                                   ← Paths.DetectGameRoot()（builder/Paths.cs:10）
  Neutraled/
    plugins/                                ← 插件根（Paths.cs:47）
      sample-hello/                         ← 一级子目录 = 一个插件（只认一级）
        plugin.json                         ← 清单（必须）
        SampleHello.dll                     ← 入口程序集（entry 指向它）
        SampleHello.cs                      ← 源码（可选，方便就地改）
        README.md
```

规则（`builder/Plugins.cs` 的 `List()`）：

| 情况 | 结果 |
|---|---|
| `plugins/<目录>/plugin.json` 存在 | 登记为插件（状态见下表） |
| 目录里**没有** `plugin.json` 但**有 `.dll`** | 登记为 `local-only`，**绝不加载**（手工放进去的 DLL） |
| 目录里既没有清单也没有 DLL | 直接跳过，不产生噪音 |
| 更深层的子目录 | 不扫描 —— 插件必须是一级子目录 |

---

## 2. plugin.json

自带的示例清单（`Neutraled/plugins/sample-hello/plugin.json`，逐字）：

```json
{
  "id": "sample-hello",
  "name": "示例插件（中文名）",
  "version": "1.0.0",
  "author": "Neutraled",
  "description": "最小可用插件：启动/关闭时写日志，在部署前统计启用 mod 数，导入后写一行日志。",
  "entry": "SampleHello.dll",
  "type": "SampleHello.HelloPlugin",
  "permissions": [
    "mods.read"
  ],
  "requires": [],
  "conflicts": [],
  "min_builder": "1.0.0"
}
```

字段全部定义在 `sdk/Neutraled.PluginSdk/Contract.cs` 的 `PluginManifest`（**契约唯一来源**）：

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | ✅ | 唯一标识。**必须等于插件目录名**，否则 `broken` 且拒绝 enable/disable（防目录穿越）。只允许字母/数字/点/下划线/连字符，≤64 字符，不能是 `.` / `..` / 系统保留名 |
| `name` | | 显示名（可中文） |
| `version` | | 默认 `"0.0.0"` |
| `author` | | 作者 |
| `description` | | 描述 |
| `entry` | ✅ | 入口程序集**相对插件目录**的路径，如 `SampleHello.dll`。越出插件目录或不存在 → 见 §7 |
| `type` | | **入口类型全名**（如 `SampleHello.HelloPlugin`）。留空 → 自动取程序集里第一个实现 `INeutraledPlugin` 的非抽象类型 |
| `permissions` | | 权限数组，见 §3 |
| `requires` | | 依赖的插件 id（决定加载顺序，缺依赖则跳过自己） |
| `conflicts` | | 互斥的插件 id（对方已加载 → 自己不加载） |
| `min_builder` | | 需要的最低 builder 版本（与 `Paths.ApiVersion()` 比较）；不满足 → `broken` |
| `enabled` | | **不在表里**：由 `--plugin-enable/--plugin-disable` 用 JsonNode **读-改-写**，所以你自己的自定义键不会被抹掉。缺省 = 启用 |

> ⚠ 两处**过时文档**，别照抄：
> * `docs/MANAGE.md:120` 的示例写 `"type": "csharp"` —— `type` 是**类型全名**，写 `csharp` 会得到
>   `[插件 ...] 找不到实现 INeutraledPlugin 的类型: csharp`。
> * `docs/MANAGE.md:130` 说"目录名不必相同，但建议一致" —— **实际是硬约束**：
>   `builder/Plugins.cs:128-132` 判 `broken`，`:236-240` 拒绝写入。

读取用 `Paths.Json`（大小写不敏感、允许注释与尾逗号），所以手写 `"Id"` / `"MinBuilder"` 也读得进来；写回时用清单自己的键名。

---

## 3. 权限（`permissions`）

```csharp
// sdk/Neutraled.PluginSdk/Contract.cs:21-36
public static class PluginPermissions
{
    public const string FilesRead  = "files.read";
    public const string FilesWrite = "files.write";
    public const string ModsRead   = "mods.read";
    public const string ModsWrite  = "mods.write";
    public const string ProcRun    = "proc.run";
    public const string NetHttp    = "net.http";
    public const string GameData   = "game.data";
}
```

| 权限 | 目前**有真检查**的调用点 | 说明 |
|---|---|---|
| `files.read` | `IPluginContext.ReadConfig` | 读 `config.json` 的键 |
| `files.write` | `IPluginContext.WriteConfig` | 写/删 `config.json` 的键；**声明了它也就放行读** |
| `mods.read` | — | 词汇表用；示例插件声明了它 |
| `mods.write` | — | 同上 |
| `proc.run` | — | 同上 |
| `net.http` | — | 同上 |
| `game.data` | — | 同上 |
| `*` | — | 全权（`HasPermission` 直接放行） |
| 其它任意串 | — | 只在诊断里警告"未知权限"，**不阻断**（允许插件自定义命名空间） |

**这是"协作式权限"，不是沙箱。** 插件是**同进程的 C# 类库** —— 一旦加载就拥有主程序的全部权限，
没有进程内沙箱能挡住它。所以本机制保证的是：

> **没声明的一定被拒**（宿主侧真检查，不是装饰），而不是"声明了就一定安全"。

未声明时的真实行为（`builder/PluginHost.cs:404-426`）：打印一行并拒绝服务，读取返回 `null`、写入不落盘：

```
[插件 <id>] 缺少权限 files.read，已拒绝
[插件 <id>] 缺少权限 files.write，已拒绝
```

> 别装来路不明的 DLL。这句不是客套。

---

## 4. 接口

```csharp
// sdk/Neutraled.PluginSdk/Contract.cs:87-118
public interface INeutraledPlugin
{
    string Id { get; }
    void OnInit(IPluginContext ctx);                        // 加载成功后立刻调用
    void OnShutdown(IPluginContext ctx);                    // 卸载前调用
    int OnHook(string hook, IPluginContext ctx);            // 非 0 = 否决/中止该操作
}

public interface IPluginContext
{
    string GameRoot { get; }
    string NeutraledRoot { get; }
    string PluginDir { get; }
    IReadOnlyList<string> Permissions { get; }
    string Language { get; }                                // 当前 builder 语言 zh/en/...
    string? Data { get; }                                   // 钩子负载（JSON 文本）；OnInit/OnShutdown 时为 null
    void SetResult(string json);                            // 否决时写给用户看的原因
    void Log(string message);                               // 前缀 [插件 <id>] 打印
    void Warn(string message);                              // 前缀 [插件 <id>] [警告] 打印
    bool HasPermission(string perm);
    string? ReadConfig(string key);                         // 需 files.read（或 files.write）
    void WriteConfig(string key, string? value);            // 需 files.write；value=null 表示删键
}
```

**推荐派生基类** `PluginBase`（`:155-174`）—— 四个成员都有默认实现，只覆写关心的部分；
它同时实现了 `IHookDeclaringPlugin`（`DeclaredHooks`），所以宿主能在加载成功时就把你的钩子清单
登记进 `--plugin-hooks` 的输出（`register\t<插件 id>\t<hook,hook>`）—— 这样
"插件加载成功" 与 "钩子清单为空" 才分得开。

```csharp
public sealed class HelloPlugin : PluginBase
{
    public override string Id => "sample-hello";                       // 必须 == 目录名 == plugin.json 的 id

    protected override IReadOnlyList<string> Hooks => new[]            // 只用于展示/文档
    {
        PluginHooks.BeforeDeploy, PluginHooks.AfterImport
    };

    public override void OnInit(IPluginContext ctx) => ctx.Log("示例插件已加载，语言=" + ctx.Language);

    public override int OnHook(string hook, IPluginContext ctx)
    {
        if (hook == PluginHooks.BeforeDeploy)
        {
            ctx.Log("部署前：负载 = " + (ctx.Data ?? "(空)"));
            ctx.SetResult(JsonSerializer.Serialize(new { ok = true, note = "示例插件不否决部署" }));
            return 0;   // 非 0 = 中止部署
        }
        return 0;
    }
}
```

---

## 5. 钩子

```csharp
// sdk/Neutraled.PluginSdk/Contract.cs:122-140
public static class PluginHooks
{
    public const string BeforeDeploy = "before_deploy";
    public const string AfterDeploy  = "after_deploy";
    public const string BeforeImport = "before_import";
    public const string AfterImport  = "after_import";
    public const string OnCommand    = "on_command";
    public const string BeforeExit   = "before_exit";
}
```

**哪些真的会被触发**（本文按 `builder/` 里的实际调用点核对）：

| 钩子 | 触发命令 | 真实调用点 | 负载（JSON） | 返回值作用 |
|---|---|---|---|---|
| `before_deploy` | `--deploy` / `--deploy-all` / `--launch` | `builder/Program.cs:1822` | `{"chapter":"chapter4"}` | **非 0 = 中止部署**，进程以该码退出 |
| `after_deploy` | 同上 | `builder/Program.cs:2277` | `{"chapter":"chapter4"}` | 忽略 |
| `before_import` | `--import-mod` 等导入路径 | `builder/ModdingImport.cs:166` | `{"input":...,"modName":...,"chapter":...}` | **非 0 = 中止导入** |
| `after_import` | 同上 | `builder/ModdingImport.cs:169` | `{"input":...,"rc":...}` | 忽略 |
| `on_command` | — | **无** | — | ⛔ **当前从不触发**（见「已知限制」第 3 条） |
| `before_exit` | — | **无** | — | ⛔ **当前从不触发** |

钩子名是**稳定标识**：它同时写进日志和插件文档，**不参与 i18n**（语言切换到 en 也是原样 `before_deploy`）。

调用模型（`PluginHost.Fire`，`PluginHost.cs:276-310`）：

* 按**依赖拓扑序**依次调用，**任一插件返回非 0 立刻中止**整条链并返回该值；`SetResult` 的内容会打印给用户。
* **未知钩子不报错**，安静返回 0（`PluginHooks.IsKnown`）。
* 没有插件 / 宿主未加载 → 安静返回 0。
* **异常 ≠ 否决**：插件抛异常只记一行日志 + 一行 `ERR` 钩子记录，**继续调用下一个插件**。
  想否决只能靠 `return 非 0`。

### 5.1 钩子调用日志（`--plugin-hooks`）

`HookLog` 是**制表符分隔的数据行**（故意不进 i18n 表，方便机器断言），上限 1000 条：

```
before_deploy	0	sample-hello	3ms
before_deploy	1	my-plugin	12ms	{"reason":"版本太旧"}
register	sample-hello	before_deploy,after_import
```

* 前 4 列：`钩子名 TAB 返回码 TAB 插件 id TAB 耗时`；非 0 时追加第 5 列 = `SetResult` 的原因。
* `ERR`（返回码列）表示该插件抛了异常。
* `register` 行 = 加载时登记的"声明钩子"，**列数不同**，不会与调用行混淆。

---

## 6. AssemblyLoadContext 加载模型

每个插件一份**可卸载**的 `AssemblyLoadContext`（`isCollectible: true`），
名字 `ntl-plugin-<id>-<6位随机>`，配 `AssemblyDependencyResolver` 解析依赖
（`builder/PluginHost.cs:153-179`）。

**隔离边界**：单插件的加载 / 初始化 / 钩子抛异常 → 只把它自己记成失败，主程序与其它插件照常往下走。
插件是最不可控的第三方代码，这里绝不能让 builder 跟着崩。

三条**实测教训**（写在 `PluginHost.cs` 类头注释里，照抄）：

1. **宿主程序集必须解析回主程序那一份。** 插件目录里常见"顺手拷一份 `ntl-builder.exe`"，
   若让解析器按目录解析，`INeutraledPlugin` 会出现**两份类型** → 强转静默失败
   （报"没有实现接口的类型"）。所以 `Resolving` 里对宿主程序集名**直接短路返回** `typeof(PluginHost).Assembly`。
   → 对应 SDK 里 `Neutraled.PluginSdk.csproj` 的 `<AssemblyName>ntl-builder</AssemblyName>` 与示例工程的
   `<ProjectReference ... Private="false" />`（**绝不**把 `ntl-builder.dll` 复制进插件目录）。
2. **`SetResult` 每次调用前必须清空。** 否则插件的"上一次写下的原因"会串台，
   在 `return 非 0` 时把上一条原因当成这次的原因打印出去 —— 排查时极易误判。
3. **钩子异常 ≠ 否决。** 异常记日志后**继续**下一个插件。

加载流程（`Load`，`PluginHost.cs:50-113`）：

1. `Plugins.List` 扫描 → **按依赖拓扑排序**（`OrderByDeps`，DFS/Kahn 变体）
2. 逐个过滤：
   * 状态不是 `enabled` → `broken` 与 `installed` 计入失败并各打一行；`disabled`/`local-only` **安静跳过**（用户自己的选择）
   * `requires` 里的插件**本批没加载成功** → `[插件 X] 缺少依赖插件: Y（跳过加载）`
   * `conflicts` 里的插件**已加载** → `[插件 X] 与已加载插件冲突: Y（跳过加载）`
3. `TryLoadOne`：校验 `entry` → 建 ALC → 找入口类型 → 实例化 → `OnInit` → 登记钩子
4. 有环只**警告**，环内按目录顺序加载：`依赖顺序有环，环内插件按目录顺序加载: <id, id>`

**卸载**（`Unload`）：**逆序**逐个 `OnShutdown`（后加载的可能依赖先加载的）→ 再逐个 `Alc.Unload()`。
可重复调用；`Load` 开头会先 `Unload`。

> 日志去**控制台**：`Paths.Log` 就是 `Console.WriteLine`（`builder/Paths.cs:109`），
> 插件的 `ctx.Log()` / `ctx.Warn()` 都打到 stdout，不写文件。

---

## 7. CLI

### 7.1 命令一览（`--help` 原文）

```
$ ntl-builder.exe --lang zh --help
  插件 / 主题 / 语言包 / 网页界面:
  --plugin-list / --plugin-enable <id> / --plugin-disable <id> / --plugin-info <id>
  --plugin-install <目录|.ntlplugin> [--force] / --plugin-remove <id> [--force] / --plugin-hooks
```

| 命令 | 作用 | 退出码 |
|---|---|---|
| `--plugin-list` | 列出全部插件（状态是 ASCII 词，可直接 grep） | 0 |
| `--plugin-info <id>` | 打印单个插件的全部字段 + 诊断 | 0 找到 / 1 找不到 / 2 id 非法 |
| `--plugin-enable <id>` | 写清单 `enabled=true`（读-改-写，保留你的自定义键） | 0 成功 / 1 失败 / 2 id 非法或与目录名不一致 |
| `--plugin-disable <id>` | 写清单 `enabled=false` | 同上（与 enable 同一实现） |
| `--plugin-install <目录\|.ntlplugin> [--force]` | 从目录或压缩包安装（zip/7z/rar 通吃） | 0 成功 / 1 失败 / 2 已存在需 `--force` |
| `--plugin-remove <id> [--force]` | 删除插件目录，**默认拒绝**（破坏性操作） | 0 成功 / 1 未删（含未加 `--force`）/ 2 id 非法 |
| `--plugin-hooks` | 加载全部插件并打印钩子日志，然后卸载 | 0 |

> ⚠ 退出码小坑：**id 非法**时 `--plugin-enable` 返回 **2**，而 `--plugin-remove` 返回 **1**
> （前者透传 `Plugins.Enable` 的码，后者在 `CliFeatures.cs:357` 被压成 `? 0 : 1`）。见 §7.3 实测。

### 7.2 真实输出

以下全部是**本机真跑过的**（`ntl-builder.exe` = `builder\bin\Release\net9.0\ntl-builder.exe`；
证据原件见 `E:\aiwork\out\Neutraled2\_feat\docs-plugins\*.txt`）。

**列清单** —— 注意 `sample-hello` 显示 `broken`（见文末已知限制第 1 条）：

```
$ ntl-builder.exe --lang zh --plugin-list
游戏根: E:\steam\steamapps\common\DELTARUNE
[语言] 已加载 7 个外部语言包: de, es, fr, ja, ko, ru, zh-tw（共 15715 条译文）
===== 插件 =====
  broken     sample-hello             1.0.0      示例插件（中文名）                Neutraled

  共 1 个插件（启用 0 / 禁用 0 / 损坏 1）
  提示: --plugin-enable <id> / --plugin-disable <id> / --plugin-info <id> / --plugin-remove <id> --force
```

列格式：`<状态,10> <id,24> <版本,10> <名称,24> <作者>`。

**看单个插件的全部字段**：

```
$ ntl-builder.exe --lang zh --plugin-info sample-hello
===== 插件: sample-hello =====
  状态: broken
  目录: E:\steam\steamapps\common\DELTARUNE\Neutraled\plugins\sample-hello
  名称: 示例插件（中文名）
  版本: 1.0.0
  作者: Neutraled
  描述: 最小可用插件：启动/关闭时写日志，在部署前统计启用 mod 数，导入后写一行日志。
  入口: SampleHello.dll
  类型: SampleHello.HelloPlugin
  权限: mods.read
  依赖: （无）
  冲突: （无）
  最低 builder: 1.0.0
```

找不到 / id 非法：

```
$ ntl-builder.exe --lang zh --plugin-info no-such-plugin
[错误] 找不到插件: no-such-plugin
$ echo $LASTEXITCODE
1

$ ntl-builder.exe --lang zh --plugin-info CON
[错误] 非法插件 id: CON（id 是系统保留名: CON）
$ echo $LASTEXITCODE
2
```

**看钩子日志**（会真的加载插件 → 调 `OnInit` → 打印 → 卸载）：

```
$ ntl-builder.exe --lang zh --plugin-hooks
===== 插件加载 =====
  [跳过] sample-hello: 清单损坏（）
  已加载 0 个插件
  有 1 个插件未加载（原因见上面各行）
[插件] 已加载 0 个插件的钩子
```

**删除的保护闸**（不加 `--force` 只提示，什么都不删）：

```
$ ntl-builder.exe --lang zh --plugin-remove sample-hello
[提示] 删除插件会移除它的全部文件: E:\steam\steamapps\common\DELTARUNE\Neutraled\plugins\sample-hello
       确认后加 --force: --plugin-remove sample-hello --force
$ echo $LASTEXITCODE
1
```

**安装**（路径不存在时）：

```
$ ntl-builder.exe --lang zh --plugin-install D:\no-such-package.ntlplugin
===== 插件安装 =====
  来源: D:\no-such-package.ntlplugin
[错误] 找不到: D:\no-such-package.ntlplugin
$ echo $LASTEXITCODE
1
```

### 7.3 id 安全闸（实测）

`IsSafeId`（`Plugins.cs:465-481`）是所有"按 id 拼路径"入口的第一道闸：

```
$ ntl-builder.exe --lang zh --plugin-enable ..
[错误] 非法插件 id: ..（id 不能是 . 或 ..）
$ echo $LASTEXITCODE
2

$ ntl-builder.exe --lang zh --plugin-remove ..
[错误] 非法插件 id: ..（id 不能是 . 或 ..）
$ echo $LASTEXITCODE
1
```

拒绝清单：空 id / 超过 64 字符 / `.` 或 `..` / 以点开头或结尾 / 含非 `[A-Za-z0-9._-]` 的字符 /
系统保留名（`CON PRN AUX NUL COM1-9 LPT1-9`）。另外两处独立的越界校验：

* `--plugin-enable/disable` 写入前再校验"**清单 id == 目录名**"，不一致直接拒绝
  （`[错误] 清单 id 与目录名不一致: 目录 X，清单 Y（拒绝写入，防目录穿越）`）
* 压缩包**逐条**校验路径（`IsSafeEntry`）：拒绝绝对路径 / 盘符 / `..` / 非法字符 / 系统保留名 /
  **以空格或点结尾**（Windows 上会被截断）。zip 里出现 `../` 是常见现象，绝不能直接拼路径写盘。

### 7.4 网页界面

`Neutraled/web` 的插件面板走只读接口 `GET /api/plugins`（`builder/WebUi.cs:510`），
返回 `Plugins.List` 的 JSON 投影（`id` / `state` / `manifest` ...）。

**GUI（`gui/`）没有插件面板** —— 仓库里 `gui/` 对 `plugin` 零引用。

---

## 8. SDK 与构建

> ⚠ **路径澄清**：SDK 在 **`Neutraled/sdk/`**，不是 `Neutraled/plugins/sdk/`（后者不存在）。

```
Neutraled/sdk/
  Neutraled.PluginSdk/
    Contract.cs                        ← 契约唯一来源（174 行，纯 BCL，可脱离 builder 独立编译）
    Neutraled.PluginSdk.csproj         ← AssemblyName = ntl-builder（★ 必须与主程序同名）
    bin/Release/net9.0/ntl-builder.dll ← 给第三方插件引用的那一个
  samples/
    SampleHello/  SampleHello.csproj   ← 链接编译 plugins/sample-hello/SampleHello.cs
    SampleClock/  SampleClock.csproj + plugin.json + SampleClock.cs   ← 第二个示例（权限自检 + 配置读写）
```

`Contract.cs` 被**两边编译**：

| 编译方 | 产物 | 作用 |
|---|---|---|
| `sdk/Neutraled.PluginSdk` | `ntl-builder.dll` | 第三方插件引用的 SDK |
| `builder/Neutraled.Builder.csproj`（`<Compile Include ... Link>`） | `ntl-builder.exe` | 主程序自己那份 |

两边 `AssemblyName` 都是 `ntl-builder`、命名空间都是 `Neutraled.Builder` —— 于是插件 DLL 里对
`ntl-builder!Neutraled.Builder.INeutraledPlugin` 的 `TypeRef` 在运行时被 `PluginHost` 的 `Resolving`
钩子短路回主程序那一份，**全程只有一份类型**，强转才成立。

**约束**（`Contract.cs:10-11` 原注释）：该文件必须能脱离 builder 独立编译 —— 只用 BCL，
不许 `using Paths / Lang / SharpCompress`，也不许引用 `UndertaleModLib`。
新增契约成员时**要一并更新 `sdk/README.md` 的钩子 / 权限表**（⚠ 该文件目前**不存在**，见已知限制第 4 条）。

### 构建示例插件

```powershell
# 在仓库根（Neutraled/）执行；产物会自动拷回 plugins\sample-hello\
dotnet build sdk\samples\SampleHello\SampleHello.csproj -c Release
# → 插件已就位: ...\Neutraled\plugins\sample-hello\SampleHello.dll
```

设计要点（`sdk/samples/SampleHello/SampleHello.csproj`）：源码仍留在插件目录里
（`<Compile Include="..\..\..\plugins\sample-hello\SampleHello.cs" Link="SampleHello.cs" />`），
插件目录里**不放** `.csproj`/`obj`/`bin`；`AfterTargets="Build"` 的 `CopyPluginToPluginDir` 把 DLL 拷回插件目录。

> 本条命令源自 `SampleHello.csproj` 的头注释与工程定义 —— **写本文档时未实跑**
> （任务约束：不跑 `dotnet build`）。

---

## 9. 加载失败排查

### 9.1 登记阶段（`--plugin-list` / `--plugin-info` 能看出来）

| 症状 / 状态 | 真实消息 | 原因 | 怎么办 |
|---|---|---|---|
| `broken` | `清单解析失败: <异常>` | `plugin.json` 不是合法 JSON | 修 JSON（读取允许注释/尾逗号，但不允许缺括号） |
| `broken` | `目录名不合法: <原因>` | 手工改名成了非法 id | 改成 `[A-Za-z0-9._-]` 且不以点开头结尾 |
| `broken` | `清单 id 与目录名不一致: 目录 X，清单 Y（拒绝启用/禁用，防目录穿越）` | id ≠ 目录名 | 让两者一致 |
| `broken` | `最低 builder 版本 X，当前 Y —— 需要升级 Neutraled` | `min_builder` 高于 `Paths.ApiVersion()`（当前 `1.0.1`） | 升级 Neutraled 或降 `min_builder` |
| `broken` | `清单没有 entry（无法加载）` | 缺 `entry` 字段 | 补上 DLL 相对路径 |
| `broken` | `入口路径不合法（越出插件目录）: <entry>` | `entry` 含 `..` / 绝对路径 | 改成插件目录内的相对路径 |
| **`installed`** | **`入口程序集缺失: <entry>`** | **`entry` 指向的 DLL 不在插件目录里**（还没构建 / 还没下载完 / 名字写错） | 构建出 DLL 放进插件目录；或修 `entry`。**这个状态是"差一步就能用"，不是坏** |
| `local-only` | `[插件] 目录里没有 plugin.json，只有 DLL —— 仅本地可见，不会被加载` | 手工丢了个 DLL 进 `plugins/` | 补 `plugin.json` |
| 警告（不阻断） | `未知权限: <权限>` | 权限串不在 `PluginPermissions.All` | 拼写检查；自定义命名空间可忽略 |
| 警告 | `依赖的插件不存在: X` / `依赖的插件已禁用: X` | `requires` 指向的插件缺失或被禁用 | 装上并启用依赖 |
| `disabled` | 无消息（安静跳过） | 清单 `enabled=false` | `--plugin-enable <id>` |

> ⚠ **入口程序集缺失 ≠ 入口程序集不存在**，是两条不同的消息：
> 前者是**登记阶段**（`Plugins.CheckEntry`，`Plugins.cs:179`，状态 = `installed`），
> 后者是**加载阶段**（`PluginHost.TryLoadOne`，`PluginHost.cs:149`）。
> 正常流程下 `installed` 会在加载前就被拦掉，所以你会看到的是 `[跳过] <id>: 缺少入口程序集`。

### 9.2 加载阶段（`--plugin-hooks` 或任何带插件的 `--deploy`）

| 真实消息 | 原因 | 怎么办 |
|---|---|---|
| `[跳过] <id>: 清单损坏（<诊断>）` | 登记结果就是 `broken` | 先按 9.1 修；括号里是诊断串 |
| `[跳过] <id>: 缺少入口程序集` | 登记结果 `installed` | 构建/放好 DLL |
| `[插件 <id>] 缺少依赖插件: X（跳过加载）` | `requires` 的插件**本批没加载成功**（注意：清单存在 ≠ 加载成功） | 先修好依赖插件 |
| `[插件 <id>] 与已加载插件冲突: X（跳过加载）` | `conflicts` 里的插件已经加载了 | 二选一 |
| `[插件 <id>] 入口程序集不存在: <entry>` | 加载时 DLL 又不见了（权限/被杀软隔离/构建中途） | 检查文件是否真的在 |
| `[插件 <id>] 找不到实现 INeutraledPlugin 的类型` | DLL 里没有实现接口的类型 | 实现 `INeutraledPlugin` 或派生 `PluginBase` |
| `[插件 <id>] 找不到实现 INeutraledPlugin 的类型: <type>` | 清单 `type` 写错了（**含抄了 `MANAGE.md` 的 `"csharp"`**） | 写**类型全名**，或留空自动探测 |
| `[插件 <id>] 无法实例化 X（需要公开的无参构造函数）` | 入口类型没有 `public` 无参构造 | 加一个 |
| `[插件 <id>] 初始化失败: <异常>` | `OnInit` 抛异常 | 看异常；这是插件自己的 bug |
| `[插件 <id>] 加载失败: <异常>` | 更外层异常（程序集加载/依赖解析等） | 看异常；常见是缺依赖 DLL |
| `[插件 <id>] 钩子异常: <异常>` | `OnHook` 抛异常。**不算否决**，会继续跑下一个插件 | 修插件；本行在日志里返回码列是 `ERR` |
| `[插件 <id>] 缺少权限 files.write，已拒绝` | 调了 `WriteConfig` 但没声明权限 | 在 `permissions` 里声明 |
| `[插件 <id>] 否决了 <hook>（返回 <n>）: <原因>` | 插件主动中止了这次部署/导入 | 看原因；摘掉该插件或改配置 |
| `依赖顺序有环，环内插件按目录顺序加载: <id, id>` | `requires` 构成环 | 只影响顺序，不阻断；建议理清依赖 |
| `[插件] 宿主加载失败: <异常>` | `PluginBoot` 自身的异常 | 极少见；宿主被 `try` 包住了，不会带崩 builder |

### 9.3 "两份类型"事故（加载失败里最阴的一种）

症状：插件明明实现了接口，却报**找不到 / 强转失败**。
根因：插件目录里出现了**第二份 `ntl-builder.dll`**（比如构建时 `Private=true` 拷了一份，
或"顺手拷个 exe/dll 过来"）。

排查：看插件目录里有没有 `ntl-builder.dll` —— **有就是错的**，删掉。
正确做法是示 example 工程那样 `<ProjectReference ... Private="false" />`，
让 `TypeRef` 在运行时被 `Resolving` 钩子短路回主程序那一份。

---

## 10. 已知限制

1. **★ 当前构建里插件根本加载不起来（真实缺陷，未修）**
   * **现象**（实测，证据 `docs-plugins/11-zh-plugin-list.txt` / `13-zh-plugin-hooks.txt`）：
     `--plugin-list` 显示 `broken`（`损坏 1`，且**诊断为空**）；`--plugin-hooks` 输出
     `[跳过] sample-hello: 清单损坏（）` + `已加载 0 个插件` → `OnInit`/`OnHook` **一次都不会跑**。
   * **根因**（`builder/Plugins.cs`）：`:99` 把新条目的 `State` **初始化成 `Broken`**；
     `:134-138` 只在 `min_builder` **不满足**时才显式赋值 `Broken`（满足时**不动** `State`）；
     `:141` 的 `CheckEntry` 只在**失败**时改 `State`；于是 `:154` 的
     `if (e.State == PluginStates.Broken) return e;` 对**每一个**清单有效 + 入口存在的插件都成立 →
     `:156` 的 `e.State = PluginStates.Enabled` **不可达**。
   * **影响面**：`enabled` 与 `disabled` 两个状态**当前不可能被观测到**；
     `--plugin-enable/--plugin-disable` 会成功写清单但**在 `--plugin-list` 里看不出区别**（仍显示 `broken`）；
     `installed`（缺入口 DLL）是唯一能正常显示的状态。整条插件链路（含钩子）等于未启用。
   * **未修**：本次任务只写文档，未改任何源码。
   * **验证方式**（修后应当看到）：`--plugin-hooks` 出现 `register\tsample-hello\tbefore_deploy,after_import`
     且 `已加载 1 个插件`；`--plugin-list` 显示 `enabled`。

2. **`SampleHello.dll` 已构建出来了**（`plugins/sample-hello/SampleHello.dll`，6144 字节，
   2026-09-26 12:26:26，晚于源码 `SampleHello.cs` 的 12:26:06）—— 所以"示例插件还没编译"**不是**
   它不加载的原因；原因就是第 1 条。

3. **两个钩子从不触发**：`on_command` 与 `before_exit` 在 `Contract.cs` 里**声明了**
   （`PluginHooks.All` 也包含它们），但整个 `builder/` 里**没有任何调用点** → 写了也永远不会被调到。
   真正会被触发的只有 `before_deploy` / `after_deploy` / `before_import` / `after_import` 四个。

4. **`sdk/README.md` 不存在**：`Contract.cs:11` 明确要求"新增契约成员时一并更新
   `sdk\README.md` 的钩子 / 权限表"，但该文件不在仓库里（`sdk/` 下只有
   `Neutraled.PluginSdk/` 与 `samples/`）。钩子与权限的权威表目前只能从 `Contract.cs` 里读，
   简版在 `docs/MANAGE.md` 第 7 节（**有两处过时**，见 §2 的警告框）。

5. **`docs/MANAGE.md` 第 7 节已过时**（本文不改它）：
   `"type": "csharp"` 示例错误；"目录名不必相同"与代码硬约束矛盾；
   `sample-hello/README.md` 里说的 `docs/MANAGE.md` 第 7 节仍然可用，但细节以本文为准。

6. **本轮未执行的命令**（它们会**改动状态**，超出"只写文档"的授权）：
   `--plugin-enable <合法 id>`、`--plugin-disable <合法 id>`、
   `--plugin-install <真实包>`、`--plugin-remove <id> --force`、以及
   `dotnet build sdk\samples\SampleHello\SampleHello.csproj`。
   它们只在 §7.1 的表里按代码说明，**没有实测输出**；上表里的"成功路径"文案
   （`[插件] 已启用 <id>` / `[插件] 已禁用 <id>` / `  已安装到: <路径>` / `  已删除: <路径>`）
   取自 `builder/Plugins.cs` 的字面量，**未在真机复现**。

7. **`SampleClock` 示例没有编译产物**（`sdk/samples/SampleClock/` 只有 `.cs`/`.csproj`/`plugin.json`，
   没有 `bin/`），也**没有**在 `plugins/` 下安装。想试它得自己构建 + `--plugin-install`，
   而按第 1 条它同样会被判 `broken`。

8. **权限不是沙箱**：7 个权限里只有 `files.read`/`files.write` 有真检查点
   （`ReadConfig`/`WriteConfig`）；`mods.*` / `proc.run` / `net.http` / `game.data`
   目前只是**词汇表**，没有对应的宿主 API 去检查它们。插件是同进程 C# 代码，一旦加载即拥有主程序全部权限。

9. **插件日志只进 stdout**：`ctx.Log/Warn` → `Paths.Log` → `Console.WriteLine`，
   **不写** `%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`（那是 GML 侧的日志）。
   GUI 启动的 builder 若没接住 stdout，插件日志就丢了。

10. **不带 `--lang` 时当前构建输出俄语**（与插件无关的 i18n 问题）：
    本机 `config.json` 里是 `"lang": "zh"`，但外部语言包（`de,es,fr,ja,ko,ru,zh-tw`）装载后
    基础表被覆盖，实测输出 `===== плагины =====` / `[Ошибка] недопустимый id плагина`。
    本文所有示例因此都显式带 `--lang zh`。该问题属于语言包特性，不在本文范围内，仅作提示。

11. **插件面板只在 Web UI**：GUI（`gui/`）对插件零引用；Web 只提供只读的 `GET /api/plugins`，
    **没有** enable/disable/install/remove 的 HTTP 接口。

---

## 附：证据文件

本文所有代码块里的命令输出都来自本机实跑，原始件在
`E:\aiwork\out\Neutraled2\_feat\docs-plugins\`：

| 文件 | 内容 |
|---|---|
| `01`–`10` | 不带 `--lang` 的同一批命令（可见第 10 条俄语现象） |
| `11-zh-plugin-list.txt` | `--lang zh --plugin-list` |
| `12-zh-plugin-info.txt` | `--lang zh --plugin-info sample-hello` |
| `13-zh-plugin-hooks.txt` | `--lang zh --plugin-hooks`（`清单损坏（）` 现场） |
| `14-zh-plugin-remove-noforce.txt` | 删除保护闸 |
| `15-zh-plugin-install-nopath.txt` | 安装路径不存在 |
| `16-zh-plugin-info-missing.txt` | `找不到插件` → exit 1 |
| `17-zh-plugin-enable-badid.txt` | `非法插件 id` → exit 2 |
| `18`/`19`/`20` | `..` / `CON` / `--plugin-remove ..` 三处 id 安全闸 |
| `21-zh-help-plugin.txt` | `--lang zh --help` 全文 |
