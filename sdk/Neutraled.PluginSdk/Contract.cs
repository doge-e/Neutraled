// Neutraled 插件契约 —— **唯一来源**。
//
// 同一个文件被两边编译：
//   * sdk\Neutraled.PluginSdk\Neutraled.PluginSdk.csproj  产出 ntl-builder.dll，给第三方插件引用
//   * builder\Neutraled.Builder.csproj（<Compile Include ... Link> 链接编译）产出主程序 ntl-builder.exe
// 两边的 AssemblyName 都是 ntl-builder、命名空间都是 Neutraled.Builder —— 于是插件 DLL 里对
// "ntl-builder!Neutraled.Builder.INeutraledPlugin" 的 TypeRef 在运行时会被 PluginHost 的 Resolving
// 钩子短路回主程序那一份，全程只有一份类型，强转才成立（见 PluginHost 类头注释「教训 1」）。
//
// ★ 约束：本文件必须能脱离 builder 独立编译 —— 只用 BCL，不许 using Paths / Lang / SharpCompress，
//   也不许引用 UndertaleModLib。新增契约成员时请一并更新 sdk\README.md 的钩子 / 权限表。
using System.Text.Json.Serialization;

namespace Neutraled.Builder;

/// <summary>插件权限词汇表（plugin.json 的 permissions 段）。
/// **协作式权限**：宿主只对自己暴露的能力做真检查（ReadConfig / WriteConfig 这类），
/// 插件是进程内 C# 代码 —— 一旦加载就拥有主程序的全部权限，没有进程内沙箱可挡。
/// 所以这里保证的是「**没声明就一定被拒**」（宿主侧真检查，不是装饰），
/// 而不是「声明了就一定安全」。取值见 SPEC §3.5：files.read/write, mods.read/write, proc.run, net.http, game.data。</summary>
public static class PluginPermissions
{
    public const string FilesRead = "files.read";
    public const string FilesWrite = "files.write";
    public const string ModsRead = "mods.read";
    public const string ModsWrite = "mods.write";
    public const string ProcRun = "proc.run";
    public const string NetHttp = "net.http";
    public const string GameData = "game.data";

    /// <summary>全部已知权限。未列出的权限只在诊断里警告，不阻断 —— 允许插件自定义命名空间。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        FilesRead, FilesWrite, ModsRead, ModsWrite, ProcRun, NetHttp, GameData
    };
}

/// <summary>插件登记状态（PluginEntry.State 的取值，纯 ASCII 数据词，便于 --plugin-list 上 grep 断言）。
///   enabled    清单有效 + 已启用（缺省）
///   disabled   清单有效 + plugin.json 的 enabled=false
///   installed  清单有效但入口程序集不存在（还没构建 / 还没下载完）
///   broken     清单解析失败、id 非法、或 id 与目录名不一致（拒绝启用/禁用）
///   local-only 目录里有 DLL 但没有 plugin.json（手工放进去的），登记但绝不加载</summary>
public static class PluginStates
{
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";
    public const string Installed = "installed";
    public const string Broken = "broken";
    public const string LocalOnly = "local-only";
}

/// <summary>插件清单（plugins/&lt;dir&gt;/plugin.json）。
/// 写盘键名统一小写下划线（与 mod.json 同一风格）；读取用 Paths.Json（大小写不敏感 + 允许注释/尾逗号）
/// → 手写 "Id" / "MinBuilder" 也读得进来，而写回时保持本表键名。
/// **enabled 不在这里**：它由 Plugins 用 JsonNode 读-改-写，保证插件自己的未知键不被覆盖。</summary>
public sealed class PluginManifest
{
    /// <summary>唯一 id（必须等于插件目录名，否则拒绝启用/禁用）。</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    /// <summary>程序集相对路径，如 plugin.dll。必须是插件目录内的相对路径（越界直接判 broken）。</summary>
    [JsonPropertyName("entry")] public string Entry { get; set; } = "";

    /// <summary>入口类型全名（如 MyPlugin.MyPluginClass）；留空 → 自动取程序集里第一个实现 INeutraledPlugin 的非抽象类型。</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>声明权限（见 PluginPermissions）。**没声明 = 宿主一律拒绝**。</summary>
    [JsonPropertyName("permissions")] public List<string> Permissions { get; set; } = new();

    /// <summary>依赖的插件 id（加载顺序按它拓扑排序；缺依赖 → 跳过该插件并说明原因）。</summary>
    [JsonPropertyName("requires")] public List<string> Requires { get; set; } = new();

    /// <summary>互斥的插件 id（已加载了对方 → 自己不再加载，避免两份钩子互相打架）。</summary>
    [JsonPropertyName("conflicts")] public List<string> Conflicts { get; set; } = new();

    /// <summary>最低 builder 版本（与 Paths.ApiVersion 比较）；不满足 → broken。</summary>
    [JsonPropertyName("min_builder")] public string MinBuilder { get; set; } = "";
}

/// <summary>插件入口接口。插件 DLL 必须引用 ntl-builder.dll（SDK 产物，AssemblyName 与主程序一致）
/// 里的这一份类型 —— 运行时宿主会强制把宿主程序集解析回主程序那一份，见 PluginHost 的 Resolving 钩子。</summary>
public interface INeutraledPlugin
{
    string Id { get; }
    void OnInit(IPluginContext ctx);
    void OnShutdown(IPluginContext ctx);

    /// <summary>钩子名见 PluginHooks；返回非 0 = 否决/中止该操作（并把原因用 SetResult 写给用户）。</summary>
    int OnHook(string hook, IPluginContext ctx);
}

/// <summary>宿主交给插件的上下文。所有敏感面（配置读写）在**调用点**再做一次权限检查。</summary>
public interface IPluginContext
{
    string GameRoot { get; }
    string NeutraledRoot { get; }
    string PluginDir { get; }
    IReadOnlyList<string> Permissions { get; }
    string Language { get; }

    /// <summary>钩子负载（JSON 文本）；OnInit/OnShutdown 时为 null。</summary>
    string? Data { get; }

    void SetResult(string json);

    /// <summary>前缀 [插件 &lt;id&gt;] 打印。</summary>
    void Log(string message);
    void Warn(string message);

    bool HasPermission(string perm);
    string? ReadConfig(string key);
    void WriteConfig(string key, string? value);
}

/// <summary>钩子名常量（与 PluginHost.Fire 的字符串一一对应）。
/// 命名用下划线，是因为它同时是**写进日志与插件文档的稳定标识**，不参与 i18n。</summary>
public static class PluginHooks
{
    public const string BeforeDeploy = "before_deploy";
    public const string AfterDeploy = "after_deploy";
    public const string BeforeImport = "before_import";
    public const string AfterImport = "after_import";
    public const string OnCommand = "on_command";
    public const string BeforeExit = "before_exit";

    /// <summary>全部已知钩子（顺序 = 文档顺序）。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        BeforeDeploy, AfterDeploy, BeforeImport, AfterImport, OnCommand, BeforeExit
    };

    /// <summary>未知钩子返回 false —— Fire 对未知钩子**不报错**、安静返回 0。</summary>
    public static bool IsKnown(string? hook) =>
        !string.IsNullOrWhiteSpace(hook) && All.Contains(hook, StringComparer.Ordinal);
}

/// <summary>可选接口：插件**声明**自己挂了哪些钩子。
/// 宿主在加载成功后就把它登记进 HookLog（--plugin-hooks 打印），
/// 这样「插件加载成功」和「钩子清单为空」这两件事就分得开了 ——
/// HookLog 原本只有 Fire 才会写，而没有任何操作触发时它是空的。
/// 派生 PluginBase 即自动实现本接口。</summary>
public interface IHookDeclaringPlugin
{
    /// <summary>声明的钩子名（建议取自 PluginHooks 常量；未声明的钩子仍会送进 OnHook）。</summary>
    IReadOnlyList<string> DeclaredHooks { get; }
}

/// <summary>插件基类（推荐写法）：三个成员都给默认实现，插件只覆写自己关心的部分。
/// 需要完全掌控加载细节时也可以直接实现 INeutraledPlugin。</summary>
public abstract class PluginBase : INeutraledPlugin, IHookDeclaringPlugin
{
    /// <summary>插件 id，必须与 plugin.json 的 id、插件目录名一致。</summary>
    public abstract string Id { get; }

    /// <summary>初始化（加载成功后立刻调用）。抛异常 → 该插件判为加载失败，其它插件不受影响。</summary>
    public virtual void OnInit(IPluginContext ctx) { }

    /// <summary>关闭（卸载前调用）。</summary>
    public virtual void OnShutdown(IPluginContext ctx) { }

    /// <summary>钩子处理：基类一律返回 0（不否决）。覆写时只处理自己关心的 hook。</summary>
    public virtual int OnHook(string hook, IPluginContext ctx) => 0;

    /// <summary>本插件实际处理的钩子（只用于展示与文档，默认「全部」）。
    /// 收窄成自己 switch 里真正处理的那几个，--plugin-hooks 的输出就与代码一致。</summary>
    protected virtual IReadOnlyList<string> Hooks => PluginHooks.All;

    IReadOnlyList<string> IHookDeclaringPlugin.DeclaredHooks => Hooks;
}
