# Neutraled 插件 SDK —— 第三方插件开发指南

本目录是插件**契约的唯一来源**。宿主 `ntl-builder.exe` 与所有插件编译的是同一份定义，因此插件作者不需要、也不应该把 SDK 的 DLL 打包进插件。

```
sdk/
  Neutraled.PluginSdk/
    Neutraled.PluginSdk.csproj   # net9.0 类库；AssemblyName = ntl-builder（必须）
    Contract.cs                  # 契约唯一来源：接口 / 基类 / 钩子常量 / 清单模型 / 权限常量
  samples/
    SampleHello/                 # 示例一：链接 plugins/sample-hello/SampleHello.cs 编译
    SampleClock/                 # 示例二：3 个钩子 + 权限自检 + 配置读写 + 打包脚本
  build-samples.ps1              # 一键构建 SDK + 两个示例，并打出 sample-clock.ntlplugin
  README.md                      # 本文件
```

## 0. 为什么 AssemblyName 必须是 ntl-builder

宿主在 `builder/PluginHost.cs:160-168` 注册了 `AssemblyLoadContext.Resolving`：凡是引用名为**宿主程序集名**的请求，一律短路回主程序里那一份类型。
SDK 的程序集名取 `ntl-builder`，于是插件 DLL 里 `INeutraledPlugin` 的 TypeRef 会落到宿主内部同一份类型上 —— 插件只需带自己的 DLL。
所以引用 SDK 时务必 `Private="false"`（不复制 SDK DLL）：

```xml
<ProjectReference Include="..\..\Neutraled.PluginSdk\Neutraled.PluginSdk.csproj" Private="false" />
```

对应地，宿主自己的 csproj 用链接编译同一份契约文件（`builder/Neutraled.Builder.csproj`）：

```xml
<Compile Include="..\sdk\Neutraled.PluginSdk\Contract.cs" Link="PluginContract.cs" />
```

## 1. 最小插件（30 秒起步）

```csharp
using Neutraled.Builder;

public sealed class HelloPlugin : PluginBase   // 也可直接实现 INeutraledPlugin
{
    public override string Id => "my-plugin";

    // 覆写 Hooks = 声明你关心的钩子（宿主加载时记录并打印）
    protected override IReadOnlyList<string> Hooks
        => new[] { PluginHooks.BeforeDeploy, PluginHooks.AfterImport };

    public override void OnInit(IPluginContext ctx) => ctx.Log("已加载，语言=" + ctx.Language);

    public override int OnHook(string hook, IPluginContext ctx)
    {
        if (hook == PluginHooks.BeforeDeploy)
        {
            ctx.SetResult("{}");   // 返回非 0 时会连同这段 JSON 一起提示用户
            return 0;               // 非 0 = 否决本次操作
        }
        return 0;
    }
}
```

csharp 项目文件要点（完整例子见 `sdk/samples/SampleHello/SampleHello.csproj`）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>MyPlugin</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Neutraled.PluginSdk\Neutraled.PluginSdk.csproj" Private="false" />
  </ItemGroup>
</Project>
```

## 2. 接口一览

| 类型 | 成员 | 说明 |
| --- | --- | --- |
| `INeutraledPlugin` | `string Id { get; }` | 插件 id，必须与插件目录名一致 |
| | `void OnInit(IPluginContext ctx)` | 加载后调用；抛异常 = 该插件加载失败（不影响其它插件） |
| | `void OnShutdown(IPluginContext ctx)` | 卸载前调用（逆序） |
| | `int OnHook(string hook, IPluginContext ctx)` | 返回值非 0 = 否决该操作 |
| `PluginBase` | 抽象类 | `Id` 抽象；`OnInit/OnShutdown/OnHook` 已有默认实现；覆写 `protected IReadOnlyList<string> Hooks` 声明钩子 |
| `IHookDeclaringPlugin` | `IReadOnlyList<string> DeclaredHooks` | 声明式钩子登记；继承 `PluginBase` 即自动实现，一般不用手写 |
| `PluginHooks` | 6 个常量 + `All` + `IsKnown(hook)` | 钩子名常量（见下表） |
| `PluginPermissions` | 7 个常量 | 权限名常量 |
| `PluginManifest` | 清单模型 | 宿主反序列化 `plugin.json` 用；插件一般不需要引 |

`IPluginContext` 成员：

| 成员 | 说明 |
| --- | --- |
| `GameRoot` | 游戏根目录 |
| `NeutraledRoot` | `<游戏根>/Neutraled` |
| `PluginDir` | 本插件目录 |
| `Permissions` | 清单里声明的权限（原样，未做校验） |
| `Language` | 当前界面语言，如 `zh` / `en` |
| `Data` | 钩子负载 JSON（无负载时为 null） |
| `SetResult(string json)` | 写入否决原因/附加信息，返回非 0 时展示给用户 |
| `Log(string)` / `Warn(string)` | 写宿主日志（`[插件 <id>] ...`），位置见 `docs/MANAGE.md` |
| `HasPermission(string)` | 声明自检；`""` 与 `"*"` 恒为 true |
| `ReadConfig(string key)` / `WriteConfig(string key, string? value)` | 读写 Neutraled 配置；**受权限强制检查**（见 §4） |

## 3. 钩子点清单

| 常量 | 名称 | 触发时机 | 当前调用点 |
| --- | --- | --- | --- |
| `PluginHooks.BeforeDeploy` | `before_deploy` | 部署章节前，返回非 0 可否决 | `builder/Program.cs:1822` |
| `PluginHooks.AfterDeploy` | `after_deploy` | 部署章节后 | `builder/Program.cs:2277` |
| `PluginHooks.BeforeImport` | `before_import` | 导入 mod 前，返回非 0 可否决 | `builder/ModdingImport.cs:166` |
| `PluginHooks.AfterImport` | `after_import` | 导入 mod 后 | `builder/ModdingImport.cs:169` |
| `PluginHooks.OnCommand` | `on_command` | 预留 | 本版本无调用点 |
| `PluginHooks.BeforeExit` | `before_exit` | 预留 | 本版本无调用点 |

- 未知钩子名：`PluginHost.Fire` 直接返回 0，不报错、不加载插件。
- 钩子抛异常：宿主捕获并写日志 `[插件 {0}] 钩子异常: {1}`，**异常不会否决操作**。
- `--plugin-hooks` 会打印每个插件登记的钩子（`register\t<id>\t<hook,hook>`），可用来确认声明生效。
- 多个插件都返回非 0 时，按加载顺序取第一个（加载顺序由 `requires` 拓扑排序决定）。

## 4. 权限字段

| 常量 | 值 | 用途 |
| --- | --- | --- |
| `PluginPermissions.FilesRead` | `files.read` | 读文件/配置 |
| `PluginPermissions.FilesWrite` | `files.write` | 写文件/配置 |
| `PluginPermissions.ModsRead` | `mods.read` | 扫描 mod |
| `PluginPermissions.ModsWrite` | `mods.write` | 修改 mod 配置 |
| `PluginPermissions.ProcRun` | `proc.run` | 起子进程 |
| `PluginPermissions.NetHttp` | `net.http` | 访问网络 |
| `PluginPermissions.GameData` | `game.data` | 读写 data.win 相关数据 |

- 未列出的权限名会在 `--plugin-list` / `--plugin-info` 里提示 `未知权限: {0}`（可用来发现拼写错误）。
- **只有 `ReadConfig` / `WriteConfig` 被宿主强制检查**：读需要 `files.read` 或 `files.write`，写需要 `files.write`；被拒时返回 null / 不写入，并写日志 `[插件 {0}] 缺少权限 {1}，已拒绝`。
- 其它权限是**协作式声明**：给用户看 + 插件自己用 `HasPermission` 自检；插件不是安全沙箱，不要安装来路不明的 DLL。

## 5. plugin.json 字段

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `id` | 是 | 插件 id；**必须与插件目录名一致**（否则状态为 broken，拒绝启停） |
| `name` | 否 | 显示名（空则用 id） |
| `version` | 否 | 默认 `0.0.0` |
| `author` | 否 | 作者 |
| `description` | 否 | 描述 |
| `entry` | 是 | 入口 DLL，相对插件目录；必须在插件目录内 |
| `type` | 否 | 入口类型的完整名，如 `MyPlugin.HelloPlugin`；留空 = 自动取程序集里第一个实现 `INeutraledPlugin` 的公开类型 |
| `permissions` | 否 | 权限名数组 |
| `requires` | 否 | 依赖的插件 id（决定加载顺序） |
| `conflicts` | 否 | 互斥的插件 id |
| `min_builder` | 否 | 需要的最低 builder 版本；高于当前版本则状态为 broken |
| `enabled` | 否 | 缺省 true；`--plugin-enable/--plugin-disable` 直接改这一项 |

目录名也有限制：ASCII 字母/数字/`.`/`_`/`-`，不超过 64 字符，不能以点开头或结尾，不能是 `CON`/`NUL` 等保留名。

## 6. 构建、打包、安装

```
dotnet build sdk\Neutraled.PluginSdk\Neutraled.PluginSdk.csproj -c Release   # 构建 SDK
dotnet build sdk\samples\SampleHello\SampleHello.csproj -c Release         # 示例一（生成后自动复制到 plugins\sample-hello\）
dotnet build sdk\samples\SampleClock\SampleClock.csproj -c Release         # 示例二
powershell -ExecutionPolicy Bypass -File sdk\build-samples.ps1            # 一次跑完上面三步 + 打包
```

手工安装：把 `plugin.json` 与插件 DLL 放进 `Neutraled/plugins/<id>/` 即可（目录名 = 清单 id）。
打包成 `.ntlplugin`（就是一个 zip，根目录直接放 `plugin.json` + DLL）：

```
Compress-Archive -Path <staging>\* -DestinationPath pkg\my-plugin.zip -Force
Move-Item pkg\my-plugin.zip pkg\my-plugin.ntlplugin
```

注意：`Compress-Archive` 只接受 `.zip` 结尾的目标名，直接写 `.ntlplugin` 会静默失败（先 zip 再改名）；也可以用 `[IO.Compression.ZipFile]::CreateFromDirectory`。

## 7. 命令行

```
ntl-builder.exe --plugin-list                          # 列出插件与状态
ntl-builder.exe --plugin-info <id>                     # 清单详情 + 诊断
ntl-builder.exe --plugin-enable  <id>
ntl-builder.exe --plugin-disable <id>
ntl-builder.exe --plugin-install <目录|.ntlplugin> [--force]
ntl-builder.exe --plugin-remove  <id> [--force]
ntl-builder.exe --plugin-hooks                         # 加载全部插件并打印登记的钩子
```

## 8. 故障排查

| 现象 | 原因 / 处理 |
| --- | --- |
| 状态 `installed` + `入口程序集缺失: X.dll` | DLL 没编译或文件名与 `entry` 不一致 |
| 状态 `broken` + `清单解析失败: ...` | `plugin.json` 不是合法 JSON |
| 状态 `broken` + `目录名不合法: ...` | 目录名含非 ASCII 字符或保留名 |
| 状态 `broken` + `清单 id 与目录名不一致` | 让两者一致（防目录穿越，拒绝启停） |
| 状态 `broken` + `最低 builder 版本 ...` | `min_builder` 高于当前 builder |
| 加载时 `找不到实现 INeutraledPlugin 的类型: X` | `type` 写错（例如误写成语言名 `csharp`），或类型不是 `public` |
| 加载时 `无法实例化 X（需要公开的无参构造函数）` | 类型缺少公开无参构造函数 |
| 加载时 `缺少依赖插件: X（跳过加载）` | `requires` 里的插件没装或被禁用 |
| 加载时 `与已加载插件冲突: X（跳过加载）` | `conflicts` 命中 |
| 日志里 `缺少权限 {0}，已拒绝` | 在 `permissions` 里补上该权限 |

## 9. 版本

- 宿主 API 版本：`Paths.ApiVersion()` = `1.0.6`（`min_builder` 与之比较）。
- SDK 程序集版本：`1.0.1.0`。
- 契约变更流程：改 `sdk/Neutraled.PluginSdk/Contract.cs` 这一处 → 重新构建 SDK 与 `ntl-builder.exe`（宿主链接编译同一文件）。
