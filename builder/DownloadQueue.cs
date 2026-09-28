using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>下载队列的一项，与 Neutraled/dl/queue.json 里的字段一一对应（落盘用 camelCase）。
/// 全部是 get/set 加初始值：队列文件是人可读、可手工编辑的，缺字段必须能宽容反序列化。</summary>
public sealed class Item
{
    /// <summary>条目 id（gb-modId-fileId-时间戳），也是命令行 --queue-* 要传的 id。</summary>
    public string Id { get; set; } = "";
    /// <summary>GameBanana 的 mod id。</summary>
    public int ModId { get; set; }
    /// <summary>mod 名称（展示用，可能为空）。</summary>
    public string ModName { get; set; } = "";
    /// <summary>文件名（不含目录，落盘文件名的最后一段）。</summary>
    public string FileName { get; set; } = "";
    /// <summary>下载直链；入队时留空也行，Run 会再问一次文件表补上。</summary>
    public string Url { get; set; } = "";
    /// <summary>状态机取值见 DownloadQueue.States。</summary>
    public string State { get; set; } = DownloadQueue.States.Queued;
    /// <summary>最后一次失败原因，成功时清空；只会写中文原文，展示时不再走 L()。</summary>
    public string Error { get; set; } = "";
    /// <summary>入队时间 yyyy-MM-ddTHH:mm:ss。</summary>
    public string Added { get; set; } = "";
    /// <summary>最后一次状态变更时间。</summary>
    public string Updated { get; set; } = "";
    /// <summary>服务端给的字节数；为 0 表示未知（下载完会按实际体积补上）。</summary>
    public long Size { get; set; }
    /// <summary>实际下载到的字节数。</summary>
    public long Got { get; set; }
    /// <summary>选定的文件 id；为空表示「第一个文件」。</summary>
    public int? FileId { get; set; }
    /// <summary>本地文件绝对路径（只会在 dl 目录下面）。</summary>
    public string LocalPath { get; set; } = "";
    /// <summary>GameBanana 的类型名（Mod / Sound / …），重新解析下载地址时要按它查文件表。</summary>
    public string Kind { get; set; } = "Mod";
}

/// <summary>
/// 下载队列（feat-gb）：Neutraled/dl/queue.json 一份清单，负责「排队 → 下载 → 交给导入器 → 标记完成」。
///
/// 设计要点（含实测教训）：
///   · 队列文件用户可见、可手工编辑，所以读取一律不进异常路径：JSON 坏了、条目 id 非法、modId 非正数都只提示并丢弃，绝不抛。
///   · 每跑完一项落盘一次（Paths.SafeWrite，带退避重试）；断电、Ctrl+C、断网中断后重跑能接着来 ——
///     已下载的项按「文件在且体积相符」直接跳过下载，接着装。
///   · 失败不删条目也不放弃队列：写 error、继续下一项；重新 Run 会重试 failed 项（Run 是显式动作，用户按一次就该尽力推进一步）。
///   · 只有 dl 目录下的文件才是本模块的领地：删文件前校验绝对路径前缀，越界一律拒绝。
///   · 下载实现复用 GbBrowse.FetchAsync（curl 优先 + HttpClient 兜底 + 10% 进度），队列里不再养第二套下载逻辑。
/// </summary>
public static class DownloadQueue
{
    /// <summary>状态机取值。写成常量而不是 enum：queue.json 里存的就是这些字符串，手工改成别的也能直接读回来。</summary>
    public static class States
    {
        /// <summary>已入队，还没开始。</summary>
        public const string Queued = "queued";
        /// <summary>正在下载。</summary>
        public const string Downloading = "downloading";
        /// <summary>下载完成，尚未（或不需要）安装。</summary>
        public const string Downloaded = "downloaded";
        /// <summary>文件已就绪，等待安装（autoInstall=false 或导入器没注入时停在这里）。</summary>
        public const string Ready = "ready-to-install";
        /// <summary>已安装完成。</summary>
        public const string Installed = "installed";
        /// <summary>失败，原因在 Error 字段里。</summary>
        public const string Failed = "failed";
        /// <summary>用户取消，Run 会跳过。</summary>
        public const string Cancelled = "cancelled";
    }

    /// <summary>落盘用的 JSON 选项：在 Paths.Json 基础上加缩进与 camelCase 键名（人要看要改）。</summary>
    private static readonly JsonSerializerOptions JsonOut = new(Paths.Json)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>queue.json 的根对象 {"items":[…]}；顺手兼容「根就是数组」的手写文件。</summary>
    private sealed class QueueFile
    {
        public List<Item> Items { get; set; } = new();
    }

    /// <summary>队列文件路径：游戏根/Neutraled/dl/queue.json。</summary>
    public static string QueuePath(string gameRoot) => Path.Combine(Paths.DownloadsRoot(gameRoot), "queue.json");

    /// <summary>读队列。文件不存在返回空表；JSON 损坏、条目 id 非法/重复、modId 非正数都只提示并丢弃。
    /// 返回的是新对象表，调用方改完要自己落盘（Save 是私有的，只由 Enqueue/Remove/Run 触发）。</summary>
    public static List<Item> List(string gameRoot)
    {
        var path = QueuePath(gameRoot);
        if (!File.Exists(path)) return new List<Item>();
        try
        {
            var text = File.ReadAllText(path);
            List<Item>? items;
            if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
                items = JsonSerializer.Deserialize<List<Item>>(text, Paths.Json);
            else
                items = JsonSerializer.Deserialize<QueueFile>(text, Paths.Json)?.Items;
            if (items == null) return new List<Item>();
            // 手工编辑过的队列文件 = 不可信输入：id 不许为空、不许带路径分隔符或 ..，也不许重复
            var clean = new List<Item>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                if (it == null) continue;
                if (string.IsNullOrWhiteSpace(it.Id) || it.Id.IndexOfAny(new[] { '/', '\\' }) >= 0 || it.Id.Contains("..", StringComparison.Ordinal))
                {
                    Paths.Log(L("[队列] 忽略 id 非法的条目：{0}", it.Id ?? ""));
                    continue;
                }
                if (!seen.Add(it.Id))
                {
                    Paths.Log(L("[队列] 忽略重复 id 的条目：{0}", it.Id));
                    continue;
                }
                if (it.ModId <= 0)
                {
                    Paths.Log(L("[队列] 忽略 mod id 非法的条目：{0}", it.Id));
                    continue;
                }
                if (!GbBrowse.IsValidType(it.Kind)) it.Kind = "Mod";
                if (string.IsNullOrWhiteSpace(it.State)) it.State = States.Queued;
                it.Id = it.Id.Trim();
                clean.Add(it);
            }
            return clean;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[队列] 队列文件损坏，按空队列处理：{0}（{1}）", path, ex.Message));
            return new List<Item>();
        }
    }

    /// <summary>落盘队列（Paths.SafeWrite，带退避重试；不用 File.WriteAllText —— 可能同时有别的 ntl-builder 在写）。</summary>
    private static void Save(string gameRoot, List<Item> items)
    {
        Directory.CreateDirectory(Paths.DownloadsRoot(gameRoot));
        Paths.SafeWrite(QueuePath(gameRoot), JsonSerializer.Serialize(new QueueFile { Items = items }, JsonOut));
    }

    /// <summary>入队一项。同 modId + 同 fileId 且未安装/未取消的条目会被复用（不重复排队）。
    /// url 或 size 为空时顺手同步问一次文件表补全；问不到也不报错（离线也能排队，Run 时会再问）。
    /// 返回入队后的条目；modId 非法时返回 Id 为空串的占位对象（调用方靠 Id.Length 判失败）。</summary>
    public static Item Enqueue(string gameRoot, int modId, string modName, int? fileId = null, string? url = null, long size = 0, string kind = "Mod")
    {
        if (modId <= 0)
        {
            Paths.Log(L("[队列] mod id 非法，拒绝入队：{0}", modId));
            return new Item { Id = "", ModId = modId };
        }
        var items = List(gameRoot);
        foreach (var old in items)
        {
            if (old.ModId == modId && old.FileId == fileId && old.State != States.Installed && old.State != States.Cancelled)
            {
                Paths.Log(L("[队列] 已存在相同的条目，直接复用：{0}", old.Id));
                return old;
            }
        }
        var now = Now();
        var it = new Item
        {
            Id = NewId(items, modId, fileId),
            ModId = modId,
            ModName = GbBrowse.OneLine(modName),
            FileId = fileId,
            Url = url?.Trim() ?? "",
            Size = size,
            Kind = GbBrowse.IsValidType(kind) ? kind.Trim() : "Mod",
            Added = now,
            Updated = now,
            State = States.Queued
        };
        if (it.Url.Length == 0 || it.Size <= 0) Resolve(it);
        items.Add(it);
        Save(gameRoot, items);
        Paths.Log(L("[队列] 已入队：{0}（mod {1}，{2}）", it.Id, modId, NameOf(it)));
        return it;
    }

    /// <summary>移除条目；deleteFile=true 时顺手删掉已下载的文件（只允许删 dl 根目录下的路径）。
    /// 返回 true = 找到并移除，false = 没有这个 id（不算异常，CLI 自己决定返回码）。</summary>
    public static bool Remove(string gameRoot, string itemId, bool deleteFile = false)
    {
        var items = List(gameRoot);
        Item? hit = null;
        foreach (var it in items)
        {
            if (string.Equals(it.Id, itemId, StringComparison.Ordinal)) { hit = it; break; }
        }
        if (hit == null)
        {
            Paths.Log(L("[队列] 队列里没有条目：{0}", itemId ?? ""));
            return false;
        }
        items.Remove(hit);
        Save(gameRoot, items);
        Paths.Log(L("[队列] 已移除条目：{0}", hit.Id));
        if (deleteFile) DeleteLocal(gameRoot, hit);
        return true;
    }

    /// <summary>顺序执行队列，每项完成即落盘，可随时中断再续跑。
    /// 已安装/已取消的项跳过；failed 的项会重试（Run 是显式动作，按一次就尽力推进一步）。
    /// autoInstall=false 时只下载，状态停在 ready-to-install，下次 Run 再装。
    /// 返回失败条数（0 = 全部成功或全跳过）。</summary>
    public static int Run(string gameRoot, bool autoInstall = true, bool deleteAfter = false)
    {
        var items = List(gameRoot);
        if (items.Count == 0)
        {
            Paths.Log(L("[队列] 队列为空"));
            return 0;
        }
        Paths.Log(L("[队列] 开始执行：{0} 项", items.Count));
        var ok = 0;
        var fail = 0;
        var skip = 0;
        foreach (var it in items)
        {
            if (it.State == States.Installed || it.State == States.Cancelled)
            {
                skip++;
                continue;
            }
            Paths.Log(L("[队列] 处理 {0}：mod {1}（{2}）", it.Id, it.ModId, NameOf(it)));
            int code;
            try
            {
                code = RunOne(gameRoot, it, autoInstall, deleteAfter);
            }
            catch (Exception ex)
            {
                it.State = States.Failed;
                it.Error = ex.Message;
                code = 1;
            }
            if (code == 0) ok++;
            else fail++;
            it.Updated = Now();
            Save(gameRoot, items);
        }
        Paths.Log(L("[队列] 执行结束：成功 {0} / 失败 {1} / 跳过 {2}", ok, fail, skip));
        return fail;
    }

    /// <summary>跑一项：本地已有文件就跳过下载，否则下载；然后按需安装。
    /// 返回 0 = 成功（含「只下载不安装」），1 = 失败。</summary>
    private static int RunOne(string gameRoot, Item it, bool autoInstall, bool deleteAfter)
    {
        it.Error = "";
        if (HasLocalFile(it))
        {
            Paths.Log(L("[队列] 跳过下载（本地已有文件）：{0}", it.Id));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(it.Url) && !Resolve(it))
            {
                it.State = States.Failed;
                it.Error = L("无法解析下载地址");
                Paths.Log(L("[队列] 失败：{0} → {1}", it.Id, it.Error));
                return 1;
            }
            if (string.IsNullOrWhiteSpace(it.Url))
            {
                it.State = States.Failed;
                it.Error = L("下载地址为空");
                Paths.Log(L("[队列] 失败：{0} → {1}", it.Id, it.Error));
                return 1;
            }
            var target = GbBrowse.DownloadPathFor(gameRoot, it.ModId, it.FileId, it.FileName);
            it.LocalPath = target;
            it.State = States.Downloading;
            it.Updated = Now();
            Paths.Log(L("[队列] 开始下载：{0} → {1}", it.Id, Path.GetFileName(target)));
            var got = GbBrowse.FetchAsync(it.Url, target, it.Size).GetAwaiter().GetResult();
            if (got < 0)
            {
                it.State = States.Failed;
                it.Error = L("下载失败");
                Paths.Log(L("[队列] 失败：{0} → {1}", it.Id, it.Error));
                return 1;
            }
            it.Got = got;
            if (it.Size <= 0) it.Size = got;   // 服务端没给体积就按实际体积记：下次幂等复用靠它
            it.State = States.Downloaded;
            it.Updated = Now();
            Paths.Log(L("[队列] 下载完成：{0}（{1} KB）", it.Id, got / 1024));
        }
        it.State = States.Ready;
        it.Updated = Now();
        if (!autoInstall)
        {
            Paths.Log(L("[队列] 已就绪，等待安装（未开启自动安装）：{0}", it.Id));
            return 0;
        }
        if (GbBrowse.ImportHook == null)
        {
            // 文件已经在本地，状态停在 ready-to-install 最诚实：下次注入好导入器再 Run 就直接装，不用重下
            it.Error = L("未注入导入器（ImportHook），无法自动安装");
            Paths.Log(L("[队列] 失败：{0} → {1}", it.Id, it.Error));
            return 1;
        }
        var code = GbBrowse.ImportHook(gameRoot, it.LocalPath, null, false);
        if (code != 0)
        {
            it.State = States.Failed;
            it.Error = L("导入失败，返回码 {0}", code);
            Paths.Log(L("[队列] 失败：{0} → {1}", it.Id, it.Error));
            return 1;
        }
        it.State = States.Installed;
        it.Error = "";
        it.Updated = Now();
        Paths.Log(L("[队列] 已安装：{0}", it.Id));
        if (deleteAfter) DeleteLocal(gameRoot, it);
        return 0;
    }

    /// <summary>打印队列（--queue-list 用）：一行一项、可 grep。状态字段保持 ASCII 原值，方便脚本判断。</summary>
    public static void PrintList(string gameRoot)
    {
        Paths.Log(L("[队列] 队列文件：{0}", QueuePath(gameRoot)));
        var items = List(gameRoot);
        if (items.Count == 0)
        {
            Paths.Log(L("[队列] 队列为空"));
            return;
        }
        Paths.Log(L("[队列] 序号 | id | mod | 名称 | 状态 | 文件 | 体积 | 已下载 | 说明"));
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            Paths.Log(L("[队列] {0} | {1} | {2} | {3} | {4} | {5} | {6} KB | {7} KB | {8}",
                i + 1, it.Id, it.ModId, GbBrowse.OneLine(it.ModName), it.State, LocalName(it),
                it.Size / 1024, it.Got / 1024, GbBrowse.OneLine(it.Error)));
        }
        Paths.Log(L("[队列] 共 {0} 项", items.Count));
    }

    /// <summary>本地文件是否已可复用：路径在、是文件、体积对得上（服务端体积为 0 时只要求非空）。</summary>
    private static bool HasLocalFile(Item it)
    {
        if (string.IsNullOrWhiteSpace(it.LocalPath)) return false;
        try
        {
            var fi = new FileInfo(it.LocalPath);
            if (!fi.Exists) return false;
            return it.Size > 0 ? fi.Length == it.Size : fi.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>问一次 GameBanana 文件表，把 Url/FileName/FileId/Size 补全。
    /// 同步等异步：CLI 是单线程线性流程，为它把整条链改成 async 不划算。
    /// 返回 true = 拿到了可用下载地址。</summary>
    private static bool Resolve(Item it)
    {
        try
        {
            var files = GameBanana.GetFilesAsync(it.ModId, it.Kind).GetAwaiter().GetResult();
            if (files.Count == 0) return false;
            GbFile? pick = null;
            if (it.FileId is > 0)
            {
                foreach (var f in files) if (f.Id == it.FileId.Value) { pick = f; break; }
                if (pick == null) Paths.Log(L("[队列] 指定的文件 id 不存在，改用第一个文件：{0}", it.FileId.Value));
            }
            pick ??= files[0];
            it.FileId = pick.Id;
            if (string.IsNullOrWhiteSpace(it.FileName)) it.FileName = GbBrowse.SafeFileName(pick.FileName);
            it.Url = pick.DownloadUrl ?? "";
            if (pick.Size > 0) it.Size = pick.Size;
            return !string.IsNullOrWhiteSpace(it.Url);
        }
        catch (Exception ex)
        {
            Paths.Log(L("[队列] 解析下载地址失败：mod {0}（{1}）", it.ModId, ex.Message));
            return false;
        }
    }

    /// <summary>只删 dl 根目录下的文件。队列文件可能被手工编辑过，LocalPath 是不可信输入，
    /// 越界路径一律拒绝（本模块的删除权限只到自己根目录）。</summary>
    private static void DeleteLocal(string gameRoot, Item it)
    {
        if (string.IsNullOrWhiteSpace(it.LocalPath)) return;
        var root = Path.GetFullPath(Paths.DownloadsRoot(gameRoot));
        string full;
        try
        {
            full = Path.GetFullPath(it.LocalPath);
        }
        catch
        {
            return;
        }
        var cmp = Platform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, cmp))
        {
            Paths.Log(L("[队列] 拒绝删除根目录之外的文件：{0}", full));
            return;
        }
        try
        {
            if (File.Exists(full))
            {
                File.Delete(full);
                Paths.Log(L("[队列] 已删除文件：{0}", full));
            }
        }
        catch (Exception ex)
        {
            Paths.Log(L("[队列] 删除文件失败：{0}（{1}）", full, ex.Message));
        }
    }

    /// <summary>生成条目 id：gb-modId-fileId-时间戳，撞了就加 -2/-3 后缀。
    /// 只含 ASCII 字母数字与短横线：id 会进命令行参数，不能带空格或路径分隔符。</summary>
    private static string NewId(List<Item> items, int modId, int? fileId)
    {
        var basis = "gb-" + modId + "-" + (fileId ?? 0) + "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
        var id = basis;
        var n = 1;
        while (items.Exists(x => string.Equals(x.Id, id, StringComparison.Ordinal)))
        {
            n++;
            id = basis + "-" + n;
        }
        return id;
    }

    /// <summary>展示用的本地文件名：优先本地路径的最后一段，没有就退回 FileName。</summary>
    private static string LocalName(Item it)
    {
        var n = string.IsNullOrEmpty(it.LocalPath) ? it.FileName : Path.GetFileName(it.LocalPath);
        return GbBrowse.OneLine(n);
    }

    /// <summary>展示用名字：队列文件可以被手工改成 modName 为 null，取名字一律走这里，避免空引用。</summary>
    private static string NameOf(Item it) => string.IsNullOrWhiteSpace(it.ModName) ? L("未命名的 mod") : it.ModName;

    /// <summary>时间戳格式：本地时间的 yyyy-MM-ddTHH:mm:ss（人看队列文件时一眼能懂，不引 UTC 时区坑）。</summary>
    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
