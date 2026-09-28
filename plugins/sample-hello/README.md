# sample-hello —— 最小示例插件

这是仓库自带的示例插件：加载/卸载时写日志，在部署前回一段 JSON，在导入后写一行日志。
它演示了插件系统的最小闭环：**清单 + 一个继承 `PluginBase` 的类 + 由 SDK 编译出的 DLL**。

## 构建（源码就在本目录，编译产物也回本目录）

```
dotnet build sdk\samples\SampleHello\SampleHello.csproj -c Release
```

`sdk/samples/SampleHello/SampleHello.csproj` 通过 `<Compile Include>` 链接本目录的 `SampleHello.cs`，
构建后由 `CopyPluginToPluginDir` 目标自动把 `SampleHello.dll` 复制回本目录。
也可以手工构建：新建 net9.0 类库，引用 `sdk/Neutraled.PluginSdk/Neutraled.PluginSdk.csproj`（`Private="false"`），把 DLL 命名为 `SampleHello.dll` 放进本目录。

## 文件

| 文件 | 说明 |
| --- | --- |
| `plugin.json` | 插件清单（`id` 必须与目录名 `sample-hello` 一致） |
| `SampleHello.cs` | 插件源码：`HelloPlugin : PluginBase` |
| `SampleHello.dll` | 构建产物（不随仓库分发时可用上面的命令生成） |

## 清单要点

```json
{
  "id": "sample-hello",
  "entry": "SampleHello.dll",
  "type": "SampleHello.HelloPlugin",
  "permissions": ["mods.read"],
  "min_builder": "1.0.0"
}
```

- `type` 必须是**入口类型的完整名**（留空则自动取第一个实现 `INeutraledPlugin` 的公开类型）。
- `permissions` 里声明 `mods.read`：示例只读 mod 数量，不需要写权限。
- 省掉 `enabled` 字段即为默认启用；`--plugin-enable/--plugin-disable` 会写这个字段。

## 声明的钩子

源码里 `protected override IReadOnlyList<string> Hooks => new[] { PluginHooks.BeforeDeploy, PluginHooks.AfterImport };`
—— 加载时宿主会登记并打印（`register\tsample-hello\tbefore_deploy,after_import`）。

## 验证

```
ntl-builder.exe --plugin-list                        # 状态应为 enabled，且无「入口程序集缺失」
ntl-builder.exe --plugin-hooks                       # 应打印 [加载] sample-hello 与 register 行
ntl-builder.exe --plugin-info sample-hello
ntl-builder.exe --plugin-disable sample-hello        # plugin.json 里 enabled 变 false
ntl-builder.exe --plugin-enable  sample-hello        # 再变回 true
```

开发第三方插件的完整指南见 `sdk/README.md`；管理文档见 `docs/MANAGE.md` 第 7 节「插件与钩子」。