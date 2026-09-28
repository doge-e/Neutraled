// 第二个示例插件：演示「声明钩子 + 权限自检 + 配置读写」。
// 与 sample-hello 的差别：它不在 plugins\ 目录里开发，而是打成一个 .ntlplugin 包，
// 用 ntl-builder --plugin-install <包> 安装 —— 这也是第三方插件的正常分发方式。
using Neutraled.Builder;

namespace SampleClock;

public sealed class ClockPlugin : PluginBase
{
    public override string Id => "sample-clock";

    /// <summary>本插件实际处理的 3 个钩子；--plugin-hooks 会把它登记成一行 register 记录。</summary>
    protected override IReadOnlyList<string> Hooks => new[]
    {
        PluginHooks.BeforeDeploy, PluginHooks.AfterDeploy, PluginHooks.BeforeExit
    };

    public override void OnInit(IPluginContext ctx)
    {
        // 权限是声明式的：plugin.json 里没写 files.read 时 HasPermission 为假，
        // 此时调用 ReadConfig 会被宿主拒绝并在日志里留下「缺少权限」记录。
        var last = ctx.HasPermission(PluginPermissions.FilesRead) ? ctx.ReadConfig("clock.last") : null;
        ctx.Log("示例时钟插件已加载；上次记录=" + (last ?? "(无)"));
    }

    public override void OnShutdown(IPluginContext ctx) => ctx.Log("示例时钟插件已卸载");

    public override int OnHook(string hook, IPluginContext ctx)
    {
        if (hook == PluginHooks.BeforeExit)
        {
            if (ctx.HasPermission(PluginPermissions.FilesWrite))
                ctx.WriteConfig("clock.last", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            ctx.Log("退出钩子：已记录时间");
            return 0;   // 非 0 = 中止退出
        }
        if (hook == PluginHooks.BeforeDeploy || hook == PluginHooks.AfterDeploy)
            ctx.Log(hook + " 负载长度=" + (ctx.Data?.Length ?? 0));
        return 0;
    }
}
