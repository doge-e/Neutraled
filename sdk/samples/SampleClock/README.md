# SampleClock —— 第二个示例插件

比 `sample-hello` 多演示三件事：**声明多个钩子、用权限自检、读写 Neutraled 配置**。

```
dotnet build sdk\samples\SampleClock\SampleClock.csproj -c Release
```

源码 `SampleClock.cs` 就在本目录，`plugin.json` 也在本目录；构建产物在 `bin/Release/net9.0/`。
`dotnet build` 后可由 `sdk\build-samples.ps1` 打包成 `dist\sample-clock.ntlplugin`。

## 行为

| 位置 | 行为 |
| --- | --- |
| 钩子声明 | `protected override IReadOnlyList<string> Hooks => new[] { PluginHooks.BeforeDeploy, PluginHooks.AfterDeploy, PluginHooks.BeforeExit };` |
| `OnInit` | `HasPermission(PluginPermissions.FilesRead)` 自检后读配置 `clock.last`，日志打印「上次记录=(…)」 |
| `OnHook(before_deploy/after_deploy)` | 打印负载长度 |
| `OnHook(before_exit)` | `HasPermission(PluginPermissions.FilesWrite)` 自检后把当前时间写入配置 `clock.last` |

## 清单

```json
{
  "id": "sample-clock",
  "entry": "SampleClock.dll",
  "type": "SampleClock.ClockPlugin",
  "permissions": ["files.read", "files.write"],
  "min_builder": "1.0.0"
}
```

## 验证

```
ntl-builder.exe --plugin-install sdk\samples\SampleClock\dist\sample-clock.ntlplugin
ntl-builder.exe --plugin-list                  # enabled；无诊断
ntl-builder.exe --plugin-hooks                 # register\tsample-clock\tbefore_deploy,after_deploy,before_exit
ntl-builder.exe --plugin-info sample-clock     # 权限: files.read, files.write
```

完整开发指南见 `sdk/README.md`。