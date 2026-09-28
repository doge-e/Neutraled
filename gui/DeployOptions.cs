namespace Neutraled.Gui;

/// <summary>
/// 部署加速档（可选开关）：勾选后给 builder 附加 --fast-deploy，
/// 跳过「输入函数重定向」那一步（chapter5 约 20 秒；命中缓存后约 1 秒）。
/// 代价：章节内「控制台输入屏蔽」失效 —— 控制台仍能打开，但游戏自身的
/// keyboard_check_direct 仍会读到按键。
///
/// 持久化位置：Neutraled/gui_deploy.txt（与 gui_lang.txt 同级、同一套约定），
/// 不动 builder 的 config.json。
/// </summary>
public static class DeployOptions
{
    /// <summary>true = 部署时附加 --fast-deploy；默认 false（保持完整部署）。</summary>
    public static bool FastDeploy { get; set; }

    /// <summary>设置文件路径（内容就是一个 0/1）。</summary>
    public static string SettingsPath(string neutraledRoot) =>
        Path.Combine(neutraledRoot, "gui_deploy.txt");

    public static void Load(string neutraledRoot)
    {
        try
        {
            var f = SettingsPath(neutraledRoot);
            if (File.Exists(f)) FastDeploy = ParseFlag(File.ReadAllText(f));
        }
        catch { /* 读不到就保持默认（关闭） */ }
    }

    public static void Save(string neutraledRoot)
    {
        try { File.WriteAllText(SettingsPath(neutraledRoot), FastDeploy ? "1" : "0"); } catch { }
    }

    /// <summary>宽松解析：1/0、true/false、on/off、yes/no 都认；其它一律当关闭。</summary>
    public static bool ParseFlag(string? text)
    {
        var v = (text ?? "").Trim().ToLowerInvariant();
        return v is "1" or "true" or "on" or "yes" or "fast";
    }

    /// <summary>把 --fast-deploy 追加到命令行；关闭时原样返回（不留多余空格）。</summary>
    public static string AppendFastDeploy(string args) =>
        FastDeploy ? args + " --fast-deploy" : args;
}
