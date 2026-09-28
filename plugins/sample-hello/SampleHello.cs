// 最小 Neutraled 插件示例 —— 编译成 SampleHello.dll 放在本目录即可（plugin.json 的 entry 指向它）。
//
// 构建（在仓库根目录）：
//   dotnet build sdk\samples\SampleHello\SampleHello.csproj -c Release
//   产物会自动拷回本目录：plugins\sample-hello\SampleHello.dll
//
// 引用的 SDK：sdk\Neutraled.PluginSdk\Neutraled.PluginSdk.csproj（产物 AssemblyName = ntl-builder）。
// 基类：Neutraled.Builder.PluginBase（也可以直接实现 Neutraled.Builder.INeutraledPlugin）。
using System.Text.Json;
using Neutraled.Builder;

namespace SampleHello;

public sealed class HelloPlugin : PluginBase
{
    public override string Id => "sample-hello";

    /// <summary>本插件实际处理的钩子（展示用；其余钩子仍会送进 OnHook，基类返回 0）。</summary>
    protected override IReadOnlyList<string> Hooks => new[]
    {
        PluginHooks.BeforeDeploy, PluginHooks.AfterImport
    };

    public override void OnInit(IPluginContext ctx)
        => ctx.Log("示例插件已加载，语言=" + ctx.Language);

    public override void OnShutdown(IPluginContext ctx)
        => ctx.Log("示例插件已卸载");

    public override int OnHook(string hook, IPluginContext ctx)
    {
        if (hook == PluginHooks.BeforeDeploy)
        {
            ctx.Log("部署前：负载 = " + (ctx.Data ?? "(空)"));
            ctx.SetResult(JsonSerializer.Serialize(new { ok = true, note = "示例插件不否决部署" }));
            return 0;   // 非 0 = 中止部署
        }
        if (hook == PluginHooks.AfterImport)
            ctx.Log("导入完成：" + (ctx.Data ?? "(空)"));
        return 0;
    }
}
