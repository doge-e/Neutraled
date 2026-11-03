using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>三态布尔：条件里出现的运行期状态（剧情 flag / 存档变量 / 输入）一律是 Unknown。</summary>
public enum TriTruth { False = 0, Unknown = 1, True = 2 }

/// <summary>一条从原版对象事件里解析出来的对话赋值（global.msg[n] = ...）。</summary>
public sealed class VanillaDialogueLine
{
    public int MsgIndex = -1;
    public string Key = "";
    /// <summary>lang 文件里解析出的可读文本；解析不到时为空（只显示 Key）。</summary>
    public string Text = "";
    public string Condition = "";
    public TriTruth Truth = TriTruth.Unknown;
    public string EventName = "";
    public string EventFriendly = "";
    /// <summary>文本里有 % 占位符（scr_84_get_subst_string），实际显示时会带入参数。</summary>
    public bool RequiresArgs;
    /// <summary>来自旧式 scr_text 展开时的 msc 编号（≥0）；用于在 UI 里标 [msc N]。</summary>
    public int MscNo = -1;
    /// <summary>true = 这一行只有控制码/运行期文本（lang 与字面量都解析不出可读正文），UI 单列，不计入可阅读行数。</summary>
    public bool RuntimeText;

    public string DisplayText
    {
        get
        {
            if (!string.IsNullOrEmpty(Text)) return Text;
            if (RuntimeText) return "（运行期文本/控制码）";
            return Key.Length > 0 ? "（无文本）" : "（运行期文本）";
        }
    }

    public string BestKey => Key;
}

/// <summary>一个原版对象事件（Create_0 / Other_10 / …）的解析结果。</summary>
public sealed class VanillaEventInfo
{
    public string Name = "";
    public string Friendly = "";
    public string CodeName = "";
    public UndertaleCode? Code;
    public bool IsInteraction;
    /// <summary>事件类型 / 子类型数字（0=Create，3=Step，8=Draw，10=Other…；用于「原版代码引用」步骤）。</summary>
    public int EventType = -1;
    public int EventSub = -1;
    /// <summary>事件实际定义所在的对象（继承时是父对象名）。</summary>
    public string OwnerObject = "";
    /// <summary>true = 本对象没定义该事件，运行的是父对象的事件（GameMaker 事件继承）。</summary>
    public bool Inherited;
    public int Chars;
    public int MsgAssignCount;
    public int DynamicCount;
    public bool Truncated;
    public string Note = "";
    public List<VanillaDialogueLine> Lines = new();
    public List<string> OtherKeys = new();
    public bool HasDialogue => Lines.Count > 0;
}

/// <summary>一个原版游戏对象的对话/交互摘要（只读，按当前房间+坐标估算条件）。</summary>
public sealed class VanillaObjectInfo
{
    public string ObjectName = "";
    public string Note = "";
    public List<string> AllEventNames = new();
    /// <summary>父对象链（直接父 → 更上层）。事件按 GameMaker 规则继承：子对象没定义的事件用父对象的。</summary>
    public List<string> ParentChain = new();
    /// <summary>对白行里来自继承事件的条数。</summary>
    public int InheritedDialogueLineCount;
    public List<VanillaEventInfo> Events = new();
    public VanillaEventInfo? InteractionEvent;
    public int DialogueLineCount;
    /// <summary>可阅读正文行数（= DialogueLineCount - RuntimeTextLineCount）。</summary>
    public int DisplayableLineCount;
    /// <summary>只有控制码/运行期文本、没有可读正文的行数。</summary>
    public int RuntimeTextLineCount;
    public int DialogueEventCount;
    public int TotalChars;
    public bool AllEventsParsed;
}

/// <summary>
/// 原版事件/对话扫描器：懒反编译 data.win 里的代码条目（Underanalyzer），把
/// <c>global.msg[n] = scr_84_get_lang_string("key")</c> 这类赋值连同所在的 if/else 条件一起抽出来，
/// 再用同目录 lang/lang_en.json（汉化包替换后即中文）解析文本。
/// 设计取舍：不做完整 AST 遍历，而是解析反编译文本——反编译输出格式稳定，按花括号深度 +
/// if/else 条件栈就能还原「哪条对话在什么条件下出现」；条件里未知的全局（global.plot 等）保持 Unknown。
/// </summary>
public sealed class VanillaCode
{
    private static readonly Regex MsgAssign = new(
        // 注意：必须排除 choicemsg[——它只是 msg[ 带前缀，旧正则 ?:global\. 可选会把 choicemsg 当 msg 抽出来。
        @"(?<![A-Za-z0-9_.])(?:global\.)?msg\[\s*(\d+)\s*\]\s*(?:\+=|=)\s*(.+?);\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MscAssign = new(
        @"global\.msc\s*=\s*(\d+)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LangKeyCall = new(
        @"scr_84_get_lang_string\(\s*""((?:[^""\\]|\\.)*)""\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LiteralOnly = new(
        @"^\s*""((?:[^""\\]|\\.)*)""\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>stringsetloc("英文文本", "lang key") —— stringset 的本地化版本。</summary>
    private static readonly Regex StringSetLocCall = new(
        @"stringsetloc\(\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>函数调用行：name(args);（参数里的逗号/括号由 TryCall 自己切）。</summary>
    private static readonly Regex FuncCallRx = new(
        @"^([A-Za-z_][A-Za-z0-9_]*)\((.*)\)\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex CaseLabelRx = new(
        @"^case\s+(\d+)\s*:\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>条件里的房间名标识符（room_forest_starwalker 这种），求值时换成 data.Rooms 索引。</summary>
    private static readonly Regex RoomIdentRx = new(
        @"(?<![A-Za-z0-9_])room_[A-Za-z0-9_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] EventTypeNames =
    {
        "Create", "Destroy", "Alarm", "Step", "Collision", "Keyboard", "Mouse", "Other",
        "Draw", "KeyPress", "KeyRelease", "Trigger", "CleanUp", "Gesture", "PreCreate",
    };

    private readonly UndertaleData _data;
    private readonly string _dataWinPath;
    private readonly Dictionary<UndertaleCode, string?> _decompileCache = new();
    private readonly Dictionary<string, int> _roomIndex = new(StringComparer.Ordinal);

    private Dictionary<string, string>? _lang;
    private string? _langPath;
    private bool _langTried;
    private bool _decompileAvailable = true;
    private bool _decompileInitTried;
    private object? _codeImporter;
    private MethodInfo? _decompileExisting;
    private string? _decompileError;
    /// <summary>单事件对白行上限（老版本是 160，会静默截断；现在放大到 4000 并把截断暴露到 UI）。</summary>
    public const int MaxLines = 4000;
    private Dictionary<int, List<VanillaDialogueLine>>? _mscCases;
    private bool _mscCasesTried;
    private bool _inMscParse;

    public VanillaCode(UndertaleData data, string dataWinPath)
    {
        _data = data;
        _dataWinPath = dataWinPath;
        try
        {
            for (int i = 0; i < data.Rooms.Count; i++)
            {
                var nm = data.Rooms[i]?.Name?.Content;
                if (!string.IsNullOrEmpty(nm) && !_roomIndex.ContainsKey(nm!)) _roomIndex[nm!] = i;
            }
        }
        catch { }
    }

    public string DataWinPath => _dataWinPath;

    /// <summary>lang 文件路径；null 表示同目录/回退目录都没有。</summary>
    public string? LangPath { get { EnsureLang(); return _langPath; } }

    public int LangEntryCount { get { EnsureLang(); return _lang?.Count ?? 0; } }

    public string LangStatus
    {
        get
        {
            EnsureLang();
            if (_langPath != null) return $"文本来源: {_langPath}（{_lang!.Count} 条）";
            return "未找到 lang/lang_en.json（只显示 lang key）";
        }
    }

    public string? DecompileError => _decompileError;

    public int RoomIndex(UndertaleRoom? room)
    {
        if (room == null) return -1;
        var nm = room.Name?.Content ?? "";
        return nm.Length > 0 && _roomIndex.TryGetValue(nm, out int idx) ? idx : -1;
    }

    /// <summary>
    /// 初始化反编译器。Underanalyzer 的 DecompileContext 只接受 IGMCode，而 UTMT 的 UndertaleCode
    /// 没有在编译期暴露该接口实现，所以走 UTMT 自带的 CodeImportGroup（public 类型、方法 internal），
    /// 这样也能复用 UndertaleData 的资产名解析（with (obj) 之类）。
    /// </summary>
    private void EnsureDecompiler()
    {
        if (_decompileInitTried) return;
        _decompileInitTried = true;
        try
        {
            var asm = typeof(UndertaleData).Assembly;
            var uaAsm = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(asm.Location) ?? "", "Underanalyzer.dll"));
            var gctxType = asm.GetType("UndertaleModLib.Decompiler.GlobalDecompileContext")
                           ?? throw new InvalidOperationException("找不到 GlobalDecompileContext");
            var gctx = Activator.CreateInstance(gctxType, _data)
                       ?? throw new InvalidOperationException("GlobalDecompileContext 创建失败");
            var cigType = asm.GetType("UndertaleModLib.Compiler.CodeImportGroup")
                          ?? uaAsm.GetType("Underanalyzer.Decompiler.CodeImportGroup")
                          ?? asm.GetTypes().FirstOrDefault(t => t.Name == "CodeImportGroup")
                          ?? uaAsm.GetTypes().FirstOrDefault(t => t.Name == "CodeImportGroup")
                          ?? throw new InvalidOperationException("找不到 CodeImportGroup");
            ConstructorInfo? ctor = null;
            foreach (var c in cigType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var ps = c.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(UndertaleData)) { ctor = c; break; }
            }
            if (ctor == null) throw new InvalidOperationException("CodeImportGroup 没有 (data, context, settings) 构造器");
            _codeImporter = ctor.Invoke(new object?[] { _data, gctx, null });
            _decompileExisting = cigType.GetMethod("DecompileExistingCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? throw new InvalidOperationException("找不到 DecompileExistingCode");
        }
        catch (Exception ex)
        {
            _decompileAvailable = false;
            _decompileError = ex.GetType().Name + ": " + ex.Message;
        }
    }

    /// <summary>反编译一个代码条目（带缓存）。失败返回 null（不影响其它条目）。</summary>
    public string? Decompile(UndertaleCode? code)
    {
        if (code == null) return "";
        if (_decompileCache.TryGetValue(code, out var cached)) return cached;
        string? text = null;
        EnsureDecompiler();
        if (_decompileAvailable && _decompileExisting != null && _codeImporter != null)
        {
            try
            {
                text = (string?)_decompileExisting.Invoke(_codeImporter, new object?[] { code });
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                _decompileError = inner.GetType().Name + ": " + inner.Message;
                text = null;
            }
        }
        _decompileCache[code] = text;
        return text;
    }

    /// <summary>
    /// 列出对象全部含代码的事件（不反编译），并按 GameMaker 事件继承规则补上父对象事件：
    /// 子对象定义了同名事件就完全覆盖父对象（不合并）；没定义的才用父对象的。
    /// </summary>
    public List<VanillaEventInfo> EnumerateEvents(UndertaleGameObject? obj)
    {
        var list = new List<VanillaEventInfo>();
        if (obj == null) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<UndertaleGameObject>();
        var cur = obj;
        int depth = 0;
        while (cur != null && depth < 12 && visited.Add(cur))
        {
            foreach (var ev in EnumerateOwnEvents(cur))
            {
                if (!seen.Add(ev.Name)) continue;
                ev.Inherited = depth > 0;
                ev.OwnerObject = cur.Name?.Content ?? "";
                list.Add(ev);
            }
            cur = cur.ParentId;
            depth++;
        }
        return list;
    }

    /// <summary>只列对象自身定义的事件（不继承）。</summary>
    private List<VanillaEventInfo> EnumerateOwnEvents(UndertaleGameObject obj)
    {
        var list = new List<VanillaEventInfo>();
        if (obj.Events == null) return list;
        for (int i = 0; i < obj.Events.Count; i++)
        {
            var sub = obj.Events[i];
            if (sub == null) continue;
            for (int j = 0; j < sub.Count; j++)
            {
                var ev = sub[j];
                if (ev?.Actions == null) continue;
                UndertaleCode? code = null;
                for (int a = 0; a < ev.Actions.Count; a++)
                {
                    var act = ev.Actions[a];
                    if (act?.CodeId != null) { code = act.CodeId; break; }
                }
                if (code == null) continue;
                string type = i >= 0 && i < EventTypeNames.Length ? EventTypeNames[i] : "Event" + i;
                // 事件序号以 EventSubtype 为准（内层列表位置 j 不等于子类型，例：Other_10 可能排在列表第 0 位）。
                int subtype = (int)ev.EventSubtype;
                string name = type + "_" + subtype;
                list.Add(new VanillaEventInfo
                {
                    Name = name,
                    Friendly = FriendlyEventName(type, subtype),
                    CodeName = code.Name?.Content ?? name,
                    Code = code,
                    EventType = i,
                    EventSub = subtype,
                    IsInteraction = type == "Other" && subtype == 10,
                });
            }
        }
        return list;
    }

    /// <summary>
    /// 扫描对象的对话事件。quick=true 时只解析最可能藏对话的事件类型（Create/Other/Alarm/KeyPress/…），
    /// 跳过 Step/Draw 这类巨型事件；查看器需要全量时传 quick=false。
    /// </summary>
    public VanillaObjectInfo ScanObject(UndertaleGameObject? obj, int roomIndex, float x, float y, bool quick = true)
    {
        var info = new VanillaObjectInfo { ObjectName = obj?.Name?.Content ?? "" };
        if (obj == null) { info.Note = "（没有对象定义）"; return info; }
        var events = EnumerateEvents(obj);
        info.AllEventNames = events.Select(e => e.Inherited ? e.Friendly + "（继承）" : e.Friendly).ToList();
        var chain = new List<string>();
        var p = obj.ParentId;
        int guard = 0;
        while (p != null && guard++ < 12)
        {
            chain.Add(GameData.Name(p.Name));
            p = p.ParentId;
        }
        info.ParentChain = chain;
        foreach (var ev in events)
        {
            bool quickCandidate = ev.IsInteraction || ev.Name.StartsWith("Create_", StringComparison.Ordinal)
                || ev.Name.StartsWith("Other_", StringComparison.Ordinal)
                || ev.Name.StartsWith("Alarm_", StringComparison.Ordinal)
                || ev.Name.StartsWith("KeyPress_", StringComparison.Ordinal)
                || ev.Name.StartsWith("Collision_", StringComparison.Ordinal)
                || ev.Name.StartsWith("PreCreate_", StringComparison.Ordinal);
            if (quick && !quickCandidate) continue;
            ParseEvent(ev, roomIndex, x, y);
            info.TotalChars += ev.Chars;
            if (ev.HasDialogue)
            {
                info.Events.Add(ev);
                info.DialogueLineCount += ev.Lines.Count;
                info.DialogueEventCount++;
                if (ev.IsInteraction) info.InteractionEvent = ev;
            }
            else if (ev.IsInteraction)
            {
                info.InteractionEvent = ev;
            }
            else if (!quick && ev.OtherKeys.Count > 0)
            {
                info.Events.Add(ev);
            }
        }
        if (!quick) info.AllEventsParsed = true;
        info.InheritedDialogueLineCount = info.Events.Where(e => e.Inherited).Sum(e => e.Lines.Count);
        info.RuntimeTextLineCount = info.Events.Sum(e => e.Lines.Count(l => l.RuntimeText));
        info.DisplayableLineCount = info.DialogueLineCount - info.RuntimeTextLineCount;
        if (info.InteractionEvent == null)
            info.InteractionEvent = events.FirstOrDefault(e => e.IsInteraction);
        if (info.DialogueLineCount == 0 && info.Events.Count == 0)
            info.Note = "（该对象事件里没有发现 global.msg 赋值）";
        return info;
    }

    /// <summary>解析单个事件的对话赋值（可重复调用；结果写回 ev.Lines）。</summary>
    public void ParseEvent(VanillaEventInfo ev, int roomIndex, float x, float y)
    {
        if (ev.Lines.Count > 0 || ev.Note.Length > 0) return;
        string? text;
        try { text = Decompile(ev.Code); }
        catch (Exception ex) { ev.Note = "反编译失败: " + ex.Message; return; }
        if (text == null)
        {
            ev.Note = "反编译不可用: " + (_decompileError ?? "未知原因");
            return;
        }
        if (text.Length == 0) { ev.Note = "（空事件）"; return; }
        ev.Chars = text.Length;
        try { ParseText(ev, text, roomIndex, x, y); }
        catch (Exception ex) { ev.Note = "解析失败: " + ex.Message; }
    }

    // ───────────────────────── 文本解析 ─────────────────────────

    private sealed class Frame
    {
        public string Kind = "if";              // if / else-if / else / with / loop / try
        public string CondText = "";
        public TriTruth Truth = TriTruth.Unknown;
        public TriTruth CondTruth = TriTruth.Unknown;
        public TriTruth ParentTruth = TriTruth.Unknown;
        public TriTruth ChainNot = TriTruth.Unknown; // 本 if/else 链里「之前的分支都不成立」
        public int CondDepth;
        public int BodyDepth = -1;
        public bool Pending;     // 条件行没有行内 '{'；反编译器把 '{' 放在下一行，等下一行再判块/单语句
        public bool HasBody => BodyDepth >= 0;
    }

    /// <summary>把一条已解析出「文本 + lang key」的对白写进 ev.Lines：语言文件优先，其次英文字面量。</summary>
    private void EmitResolved(VanillaEventInfo ev, int msgIdx, string? literal, string? key,
        bool requiresArgs, string cond, TriTruth truth, int mscNo, int cap)
    {
        if (ev.Lines.Count >= cap) { ev.Truncated = true; return; }
        ev.MsgAssignCount++;
        string text = !string.IsNullOrEmpty(key) ? ResolveKey(key!) : "";
        if (text.Length == 0 && literal != null) text = Clean(UnescapeGml(literal));
        // 纯控制码文本（例 lang 值只有 "\C2 "）清洗后为空：只要源里确实写了字符串字面量/有 lang key，
        // 就保留这一行并标成「运行期文本/控制码」，不混进可阅读正文计数（独立审计要求分开报）。
        if (text.Length == 0 && string.IsNullOrEmpty(key) && string.IsNullOrEmpty(literal)) { ev.DynamicCount++; return; }
        bool runtimeOnly = text.Length == 0;
        ev.Lines.Add(new VanillaDialogueLine
        {
            RuntimeText = runtimeOnly,
            MsgIndex = msgIdx,
            Key = key ?? "",
            Text = text,
            Condition = cond,
            Truth = truth,
            EventName = ev.Name,
            EventFriendly = ev.Friendly,
            RequiresArgs = requiresArgs,
            MscNo = mscNo,
        });
    }

    /// <summary>切一行函数调用 name(a, b, c);（字符串里的逗号/括号不切）。</summary>
    private static bool TryCall(string raw, out string name, out List<string> args)
    {
        name = "";
        args = new List<string>();
        var m = FuncCallRx.Match(raw.Trim());
        if (!m.Success) return false;
        name = m.Groups[1].Value;
        string inner = m.Groups[2].Value;
        var sb = new StringBuilder();
        int depth = 0;
        bool inStr = false;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < inner.Length) sb.Append(inner[++i]);
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '(' || c == '[') { depth++; sb.Append(c); continue; }
            if (c == ')' || c == ']') { depth--; sb.Append(c); continue; }
            if (c == ',' && depth == 0) { args.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        string tail = sb.ToString().Trim();
        if (tail.Length > 0) args.Add(tail);
        return true;
    }

    /// <summary>参数是纯字符串字面量时返回其解码值，否则 null。</summary>
    private static string? StrLit(string arg)
    {
        var m = LiteralOnly.Match(arg);
        return m.Success ? UnescapeGml(m.Groups[1].Value) : null;
    }

    /// <summary>msgsetsub 的格式串：把 % 换成 {0}/{1} 占位，避免和显示层混淆。</summary>
    private static string FormatPlaceholders(string fmt)
    {
        int n = 0;
        return Regex.Replace(fmt, "%", _ => "{" + (n++) + "}");
    }

    /// <summary>
    /// 解析旧式 scr_text(global.msc) 的 case 文本：文本就在 data.win 的 scr_text 脚本里，
    /// switch(arg0) 每个 case 是一串 global.msg[n] = ...；按 case 切块后用同一个 ParseText 解析，
    /// 结果缓存成模板，调用点再按自己的条件/三态拼起来。
    /// </summary>
    private Dictionary<int, List<VanillaDialogueLine>>? GetMscCases()
    {
        if (_mscCasesTried) return _mscCases;
        _mscCasesTried = true;
        var dict = new Dictionary<int, List<VanillaDialogueLine>>();
        try
        {
            UndertaleCode? code = null;
            if (_data.Code != null)
            {
                foreach (var c in _data.Code)
                {
                    var nm = c?.Name?.Content ?? "";
                    if (nm == "scr_text" || nm.EndsWith("_scr_text", StringComparison.Ordinal)) { code = c; break; }
                }
            }
            string? text = code != null ? Decompile(code) : null;
            if (string.IsNullOrEmpty(text)) { _mscCases = dict; return dict; }
            var lines = text!.Split('\n');
            int depth = 0;
            int curCase = -1;
            var bodies = new Dictionary<int, StringBuilder>();
            foreach (var raw in lines)
            {
                string t = StripStrings(raw).Trim();
                if (depth == 2)
                {
                    var cm = CaseLabelRx.Match(t);
                    if (cm.Success)
                    {
                        if (int.TryParse(cm.Groups[1].Value, out int cn)) { curCase = cn; bodies[cn] = new StringBuilder(); }
                        continue;
                    }
                    if (t == "break;") { curCase = -1; continue; }
                }
                if (curCase >= 0) bodies[curCase].AppendLine(raw);
                foreach (char ch in t)
                {
                    if (ch == '{') depth++;
                    else if (ch == '}') depth = Math.Max(0, depth - 1);
                }
            }
            foreach (var kv in bodies)
            {
                var temp = new VanillaEventInfo { Name = "scr_text", Friendly = "旧式 scr_text 文本" };
                _inMscParse = true;
                try { ParseText(temp, kv.Value.ToString(), -1, 0f, 0f); }
                catch { }
                finally { _inMscParse = false; }
                if (temp.Lines.Count > 0) dict[kv.Key] = temp.Lines;
            }
        }
        catch (Exception ex)
        {
            _decompileError ??= "scr_text 解析失败: " + ex.Message;
        }
        _mscCases = dict;
        return dict;
    }

    private void ParseText(VanillaEventInfo ev, string text, int roomIndex, float px, float py)
    {
        var lines = text.Split('\n');
        var stack = new List<Frame>();
        Frame? lastClosed = null;
        int depth = 0;
        int cap = MaxLines;
        int msgNo = 0;          // 模拟 global.msgno：msgsetloc 设索引、msgnextloc 自增
        var pendingMsc = new List<(int No, string Cond, TriTruth Truth)>(); // 旧式 global.msc = N 候选（可能来自不同分支）

        string CondDisplay(List<Frame> st)
        {
            var parts = new List<string>();
            for (int i = 0; i < st.Count; i++)
            {
                var f = st[i];
                if (f.Kind == "with") parts.Add("within with(...)");
                else if (f.Kind == "loop" || f.Kind == "try") parts.Add(f.CondText);
                else if (f.CondText.Length > 0) parts.Add(f.CondText);
            }
            var s = string.Join(" && ", parts);
            return s.Length > 220 ? s[..217] + "..." : s;
        }

        // 反编译器风格：条件行与 '{' 分行。若「没有花括号」的条件行后面跟的又是条件行
        // （单语句嵌套，如 if (a) if (b) { ... }），把挂起帧记成下一层块的 BodyDepth，
        // 让内层 '}' 关闭时一起弹出，避免外层条件丢失。
        void AdoptPending()
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var pf = stack[i];
                if (pf.Pending && !pf.HasBody) { pf.Pending = false; pf.BodyDepth = depth + 1; }
                else break;
            }
        }

        static string CombineConds(string a, string b)
        {
            if (a.Length == 0) return b;
            if (b.Length == 0 || b == a) return a;
            if (b.Contains(a, StringComparison.Ordinal)) return b;
            if (a.Contains(b, StringComparison.Ordinal)) return a;
            var s = a + " && " + b;
            return s.Length > 240 ? s[..237] + "..." : s;
        }

        void AddLine(int msgIdx, string rhs, string cond, TriTruth truth)
        {
            if (ev.Lines.Count >= cap) { ev.Truncated = true; return; }
            ev.MsgAssignCount++;
            var keys = new List<string>();
            foreach (Match km in LangKeyCall.Matches(rhs))
            {
                var k = UnescapeGml(km.Groups[1].Value);
                if (!keys.Contains(k)) keys.Add(k);
            }
            bool requiresArgs = rhs.Contains("scr_84_get_subst_string", StringComparison.Ordinal);
            if (keys.Count > 0)
            {
                foreach (var k in keys)
                {
                    string t = ResolveKey(k);
                    ev.Lines.Add(new VanillaDialogueLine
                    {
                        RuntimeText = t.Length == 0,
                        MsgIndex = msgIdx,
                        Key = k,
                        Text = t,
                        Condition = cond,
                        Truth = truth,
                        EventName = ev.Name,
                        EventFriendly = ev.Friendly,
                        RequiresArgs = requiresArgs,
                    });
                }
                return;
            }
            var ss = StringSetLocCall.Match(rhs);
            if (ss.Success)
            {
                ev.MsgAssignCount--;
                EmitResolved(ev, msgIdx, UnescapeGml(ss.Groups[1].Value), UnescapeGml(ss.Groups[2].Value),
                    false, cond, truth, -1, cap);
                return;
            }
            var lm = LiteralOnly.Match(rhs);
            if (lm.Success)
            {
                string lit = Clean(UnescapeGml(lm.Groups[1].Value));
                ev.Lines.Add(new VanillaDialogueLine
                {
                    RuntimeText = lit.Length == 0,
                    MsgIndex = msgIdx,
                    Key = "",
                    Text = lit,
                    Condition = cond,
                    Truth = truth,
                    EventName = ev.Name,
                    EventFriendly = ev.Friendly,
                });
                return;
            }
            ev.DynamicCount++;
        }

        // ch2 的本地化辅助函数：msgsetloc/msgnextloc/msgsetsubloc/msgnextsubloc 以及旧式 scr_text 展开。
        bool HandleMsgCall(string fn, List<string> a, string cond, TriTruth truth)
        {
            string baseName = fn.StartsWith("c_", StringComparison.Ordinal) ? fn[2..] : fn;
            switch (baseName)
            {
                case "msgsetloc":
                case "msgset":
                {
                    if (a.Count < 2 || !int.TryParse(a[0], out int idx)) return false;
                    msgNo = idx;
                    pendingMsc.Clear();
                    string? key = a.Count >= 3 ? StrLit(a[^1]) : null;
                    string? lit = StrLit(a[1]);
                    EmitResolved(ev, idx, lit, key, false, cond, truth, -1, cap);
                    return true;
                }
                case "msgnextloc":
                case "msgnext":
                {
                    if (a.Count < 1) return false;
                    string? lit = StrLit(a[0]);
                    string? key = a.Count >= 2 ? StrLit(a[^1]) : null;
                    msgNo++;
                    EmitResolved(ev, msgNo, lit, key, false, cond, truth, -1, cap);
                    return true;
                }
                case "msgsetsubloc":
                {
                    if (a.Count < 3 || !int.TryParse(a[0], out int idx)) return false;
                    msgNo = idx;
                    pendingMsc.Clear();
                    string? fmt = StrLit(a[1]);
                    string? key = StrLit(a[^1]);
                    EmitResolved(ev, idx, fmt != null ? FormatPlaceholders(fmt) : null, key, true, cond, truth, -1, cap);
                    return true;
                }
                case "msgnextsubloc":
                {
                    if (a.Count < 1) return false;
                    string? fmt = StrLit(a[0]);
                    string? key = a.Count >= 2 ? StrLit(a[^1]) : null;
                    msgNo++;
                    EmitResolved(ev, msgNo, fmt != null ? FormatPlaceholders(fmt) : null, key, true, cond, truth, -1, cap);
                    return true;
                }
                case "scr_text":
                {
                    if (a.Count >= 1 && int.TryParse(a[0], out int litNo) && litNo > 0)
                    {
                        ExpandMsc(ev, litNo, cond, truth, cap);
                    }
                    else if (pendingMsc.Count > 0)
                    {
                        // global.msc 常在各分支里赋值、scr_text 在分支外统一调用：
                        // 每个候选按「赋值点条件 + 调用点条件」分别展开，避免条件丢失。
                        foreach (var c in pendingMsc)
                        {
                            ExpandMsc(ev, c.No, CombineConds(c.Cond, cond), And(c.Truth, truth), cap);
                        }
                    }
                    pendingMsc.Clear();
                    return true;
                }
                default:
                    return false;
            }
        }

        void ExpandMsc(VanillaEventInfo target, int mscNo, string cond, TriTruth truth, int lineCap)
        {
            if (_inMscParse) return; // scr_text 自身的 case 体不再递归展开
            var cases = GetMscCases();
            if (cases == null || !cases.TryGetValue(mscNo, out var tmpl) || tmpl.Count == 0) return;
            foreach (var t in tmpl)
            {
                if (target.Lines.Count >= lineCap) { target.Truncated = true; break; }
                target.MsgAssignCount++;
                string c = string.IsNullOrEmpty(t.Condition) ? cond
                    : (string.IsNullOrEmpty(cond) ? t.Condition : cond + " && " + t.Condition);
                target.Lines.Add(new VanillaDialogueLine
                {
                    RuntimeText = t.RuntimeText || t.Text.Length == 0,
                    MsgIndex = t.MsgIndex,
                    Key = t.Key,
                    Text = t.Text,
                    Condition = c,
                    Truth = And(truth, t.Truth),
                    EventName = target.Name,
                    EventFriendly = target.Friendly,
                    RequiresArgs = t.RequiresArgs,
                    MscNo = mscNo,
                });
            }
        }

        for (int idx = 0; idx < lines.Length; idx++)
        {
            string raw = lines[idx].TrimEnd('\r');
            string t = StripStrings(raw).Trim();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)) continue;
            // 行首的 }（可能连续多个），关闭对应条件帧
            while (t.Length > 0 && t[0] == '}')
            {
                depth = Math.Max(0, depth - 1);
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    var f = stack[i];
                    if (f.BodyDepth > depth)
                    {
                        lastClosed = f;
                        stack.RemoveAt(i);
                    }
                }
                int cut = raw.IndexOf('}');
                raw = cut >= 0 ? raw[(cut + 1)..].TrimStart() : "";
                t = StripStrings(raw).Trim();
            }
            if (t.Length == 0) continue;

            // 单独的 { → 上一条件帧的块开始
            if (t == "{")
            {
                depth++;
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].BodyDepth < 0 && stack[i].Pending) { stack[i].Pending = false; stack[i].BodyDepth = depth; break; }
                }
                continue;
            }

            // 条件 / 结构行
            if (t.StartsWith("if (", StringComparison.Ordinal) || t.StartsWith("if(", StringComparison.Ordinal))
            {
                string cond = ExtractParen(t, t.IndexOf('('));
                AdoptPending();
                var parent = Effective(stack);
                var condTruth = Evaluate(cond, roomIndex, px, py, InWith(stack));
                var f = new Frame
                {
                    Kind = "if",
                    CondText = cond,
                    CondTruth = condTruth,
                    ParentTruth = parent,
                    Truth = And(parent, condTruth),
                    ChainNot = Not(condTruth),
                    CondDepth = depth,
                    Pending = !t.Contains('{'),
                };
                stack.Add(f);
                if (t.Contains('{')) { depth++; f.BodyDepth = depth; }
                continue;
            }
            if (t.StartsWith("else if (", StringComparison.Ordinal) || t.StartsWith("else if(", StringComparison.Ordinal))
            {
                string cond = ExtractParen(t, t.IndexOf('('));
                AdoptPending();
                var prev = LastIfFrame(stack, lastClosed, depth);
                var parent = prev?.ParentTruth ?? Effective(stack);
                var prevNot = prev?.ChainNot ?? TriTruth.Unknown;
                var condTruth = Evaluate(cond, roomIndex, px, py, InWith(stack));
                var f = new Frame
                {
                    Kind = "else-if",
                    CondText = cond,
                    CondTruth = condTruth,
                    ParentTruth = parent,
                    Truth = And(And(parent, prevNot), condTruth),
                    ChainNot = And(prevNot, Not(condTruth)),
                    CondDepth = depth,
                    Pending = !t.Contains('{'),
                };
                stack.Add(f);
                if (t.Contains('{')) { depth++; f.BodyDepth = depth; }
                lastClosed = null;
                continue;
            }
            if (t == "else" || t.StartsWith("else {", StringComparison.Ordinal) || t.StartsWith("else{", StringComparison.Ordinal))
            {
                AdoptPending();
                var prev = LastIfFrame(stack, lastClosed, depth);
                var parent = prev?.ParentTruth ?? Effective(stack);
                var prevNot = prev?.ChainNot ?? TriTruth.Unknown;
                var f = new Frame
                {
                    Kind = "else",
                    CondText = "else",
                    ParentTruth = parent,
                    Truth = And(parent, prevNot),
                    ChainNot = TriTruth.False,
                    CondDepth = depth,
                    Pending = !t.Contains('{'),
                };
                stack.Add(f);
                if (t.Contains('{')) { depth++; f.BodyDepth = depth; }
                lastClosed = null;
                continue;
            }
            if (t.StartsWith("with (", StringComparison.Ordinal) || t.StartsWith("with(", StringComparison.Ordinal))
            {
                string inner = ExtractParen(t, t.IndexOf('('));
                AdoptPending();
                var f = new Frame
                {
                    Kind = "with",
                    CondText = "with(" + inner + ")",
                    Truth = TriTruth.Unknown,
                    ParentTruth = Effective(stack),
                    CondDepth = depth,
                    Pending = !t.Contains('{'),
                };
                stack.Add(f);
                if (t.Contains('{')) { depth++; f.BodyDepth = depth; }
                continue;
            }
            if (t.StartsWith("for (", StringComparison.Ordinal) || t.StartsWith("while (", StringComparison.Ordinal)
                || t.StartsWith("repeat (", StringComparison.Ordinal) || t.StartsWith("switch (", StringComparison.Ordinal)
                || t == "try" || t.StartsWith("try {", StringComparison.Ordinal))
            {
                string label = t.StartsWith("try", StringComparison.Ordinal) ? "try" : t[..Math.Max(0, t.IndexOf('('))].Trim() + "(...)";
                AdoptPending();
                var f = new Frame
                {
                    Kind = t.StartsWith("try", StringComparison.Ordinal) ? "try" : "loop",
                    CondText = label,
                    Truth = TriTruth.Unknown,
                    ParentTruth = Effective(stack),
                    CondDepth = depth,
                    Pending = !t.Contains('{'),
                };
                stack.Add(f);
                if (t.Contains('{')) { depth++; f.BodyDepth = depth; }
                continue;
            }

            // 普通语句行：先试 ch2 的本地化函数（msgsetloc/msgnextloc/msgsetsub…/scr_text），
            // 再抽 global.msg[n] = ...，最后记 global.msc = N（等后面的 scr_text 调用再展开）。
            string condText = CondDisplay(stack);
            var truth = Effective(stack);
            if (TryCall(raw.Trim(), out var fn, out var cargs) && HandleMsgCall(fn, cargs, condText, truth))
            {
                // 已由本地化函数路径处理
            }
            else
            {
                var mm = MsgAssign.Match(raw);
                if (mm.Success && int.TryParse(mm.Groups[1].Value, out int msgIdx))
                {
                    AddLine(msgIdx, mm.Groups[2].Value, condText, truth);
                }
                else
                {
                    var msc = MscAssign.Match(raw);
                    if (msc.Success && int.TryParse(msc.Groups[1].Value, out int mscNo))
                    {
                        if (mscNo > 0)
                        {
                            if (!pendingMsc.Any(p => p.No == mscNo && p.Cond == condText))
                                pendingMsc.Add((mscNo, condText, truth));
                        }
                        else pendingMsc.Clear(); // global.msc = 0 重置
                    }
                }
            }

            // 单语句 if 体（反编译输出里极少见）：执行完这一行后关闭挂起的条件帧
            while (stack.Count > 0)
            {
                var top = stack[^1];
                if (!top.Pending || top.HasBody) break;
                lastClosed = top;
                stack.RemoveAt(stack.Count - 1);
            }
        }

        foreach (Match km in LangKeyCall.Matches(text))
        {
            var k = UnescapeGml(km.Groups[1].Value);
            if (!ev.Lines.Any(l => l.Key == k) && !ev.OtherKeys.Contains(k)) ev.OtherKeys.Add(k);
        }
        if (ev.OtherKeys.Count > 40) ev.OtherKeys = ev.OtherKeys.Take(40).ToList();
    }

    private static Frame? LastIfFrame(List<Frame> stack, Frame? lastClosed, int depth)
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            var f = stack[i];
            if (f.CondDepth == depth && (f.Kind == "if" || f.Kind == "else-if")) return f;
        }
        if (lastClosed != null && lastClosed.CondDepth == depth
            && (lastClosed.Kind == "if" || lastClosed.Kind == "else-if")) return lastClosed;
        return null;
    }

    private static bool InWith(List<Frame> stack) => stack.Any(f => f.Kind == "with" || f.Kind == "loop");

    private static TriTruth Effective(List<Frame> stack)
    {
        var r = TriTruth.True;
        foreach (var f in stack) r = And(r, f.Truth);
        return r;
    }

    private static TriTruth And(TriTruth a, TriTruth b)
    {
        if (a == TriTruth.False || b == TriTruth.False) return TriTruth.False;
        if (a == TriTruth.Unknown || b == TriTruth.Unknown) return TriTruth.Unknown;
        return TriTruth.True;
    }

    private static TriTruth Or(TriTruth a, TriTruth b)
    {
        if (a == TriTruth.True || b == TriTruth.True) return TriTruth.True;
        if (a == TriTruth.Unknown || b == TriTruth.Unknown) return TriTruth.Unknown;
        return TriTruth.False;
    }

    private static TriTruth Not(TriTruth a) => a == TriTruth.Unknown ? TriTruth.Unknown : (a == TriTruth.True ? TriTruth.False : TriTruth.True);

    // ───────────────────────── 表达式求值（GML 子集） ─────────────────────────

    private readonly record struct Val(int Kind, double Num, string Str, bool Bool, TriTruth Truth)
    {
        public static readonly Val Unknown = new(0, 0, "", false, TriTruth.Unknown);
        public static Val N(double n) => new(1, n, "", false, TriTruth.Unknown);
        public static Val S(string s) => new(2, 0, s, false, TriTruth.Unknown);
        public static Val B(bool b) => new(3, 0, "", b, b ? TriTruth.True : TriTruth.False);
        public static Val T(TriTruth t) => new(4, 0, "", false, t);
        public TriTruth AsTruth()
        {
            if (Kind == 4) return Truth;
            if (Kind == 3) return Bool ? TriTruth.True : TriTruth.False;
            if (Kind == 1) return Math.Abs(Num) > 0.0000001 ? TriTruth.True : TriTruth.False;
            if (Kind == 2) return Str.Length > 0 ? TriTruth.True : TriTruth.False;
            return TriTruth.Unknown;
        }
    }

    private TriTruth Evaluate(string expr, int roomIndex, float px, float py, bool inWith)
    {
        try
        {
            // ch2 有些条件把房间名当标识符用（room_forest_starwalker）；能查到索引就替换成数字。
            if (expr.Contains("room_", StringComparison.Ordinal))
            {
                expr = RoomIdentRx.Replace(expr, m =>
                    _roomIndex.TryGetValue(m.Value, out int ri) ? ri.ToString(CultureInfo.InvariantCulture) : m.Value);
            }
            var p = new ExprParser(expr, roomIndex, px, py, inWith);
            var v = p.ParseTernary();
            if (!p.Ok) return TriTruth.Unknown;
            return v.AsTruth();
        }
        catch
        {
            return TriTruth.Unknown;
        }
    }

    private sealed class ExprParser
    {
        private readonly string _s;
        private readonly int _room;
        private readonly float _x, _y;
        private readonly bool _hasX, _hasY;   // NaN 坐标 = 未知（对象级查看/自检没有实例坐标时，x/y 求值应为 Unknown 而不是具体数值）
        private readonly bool _inWith;
        private int _p;
        public bool Ok = true;

        public ExprParser(string s, int room, float x, float y, bool inWith)
        {
            _s = s; _room = room; _x = x; _y = y; _inWith = inWith;
            _hasX = !float.IsNaN(x); _hasY = !float.IsNaN(y);
        }

        private void Ws() { while (_p < _s.Length && char.IsWhiteSpace(_s[_p])) _p++; }
        private bool Take(string op)
        {
            Ws();
            if (string.CompareOrdinal(_s, _p, op, 0, op.Length) != 0) return false;
            _p += op.Length;
            return true;
        }
        private bool Peek(string op)
        {
            Ws();
            return string.CompareOrdinal(_s, _p, op, 0, op.Length) == 0;
        }

        public Val ParseTernary()
        {
            var c = ParseOr();
            Ws();
            if (_p < _s.Length && _s[_p] == '?')
            {
                _p++;
                var a = ParseTernary();
                if (!Take(":")) { Ok = false; return Val.Unknown; }
                var b = ParseTernary();
                var ct = c.AsTruth();
                if (ct == TriTruth.True) return a;
                if (ct == TriTruth.False) return b;
                return Val.Unknown;
            }
            return c;
        }

        private Val ParseOr()
        {
            var v = ParseAnd();
            while (true)
            {
                if (Take("||"))
                {
                    var r = ParseAnd();
                    v = Val.T(Or(v.AsTruth(), r.AsTruth()));
                }
                else break;
            }
            return v;
        }

        private Val ParseAnd()
        {
            var v = ParseCmp();
            while (true)
            {
                if (Take("&&"))
                {
                    var r = ParseCmp();
                    v = Val.T(And(v.AsTruth(), r.AsTruth()));
                }
                else break;
            }
            return v;
        }

        private Val ParseCmp()
        {
            var a = ParseAdd();
            while (true)
            {
                if (Take("==") || Take("="))
                {
                    var b = ParseAdd();
                    a = CompareEq(a, b, false);
                }
                else if (Take("!=") || Take("<>"))
                {
                    var b = ParseAdd();
                    a = CompareEq(a, b, true);
                }
                else if (Take("<=")) a = CompareNum(a, ParseAdd(), (x, y) => x <= y);
                else if (Take(">=")) a = CompareNum(a, ParseAdd(), (x, y) => x >= y);
                else if (Take("<")) a = CompareNum(a, ParseAdd(), (x, y) => x < y);
                else if (Take(">")) a = CompareNum(a, ParseAdd(), (x, y) => x > y);
                else break;
            }
            return a;
        }

        private static Val CompareEq(Val a, Val b, bool negate)
        {
            if (a.Kind == 0 || b.Kind == 0) return Val.Unknown;
            if (a.Kind == 4 || b.Kind == 4)
            {
                var ta = a.AsTruth();
                var tb = b.AsTruth();
                if (ta == TriTruth.Unknown || tb == TriTruth.Unknown) return Val.Unknown;
                bool eqb = ta == tb;
                return Val.B(negate ? !eqb : eqb);
            }
            bool eq;
            if (a.Kind == 2 || b.Kind == 2)
            {
                if (a.Kind != b.Kind) return Val.B(negate);
                eq = string.Equals(a.Str, b.Str, StringComparison.Ordinal);
            }
            else if (a.Kind == 3 || b.Kind == 3)
            {
                var ta = a.AsTruth();
                var tb = b.AsTruth();
                if (ta == TriTruth.Unknown || tb == TriTruth.Unknown) return Val.Unknown;
                eq = ta == tb;
            }
            else
            {
                eq = Math.Abs(a.Num - b.Num) < 0.0000001;
            }
            return Val.B(negate ? !eq : eq);
        }

        private static Val CompareNum(Val a, Val b, Func<double, double, bool> f)
        {
            if (a.Kind != 1 || b.Kind != 1) return Val.Unknown;
            return Val.B(f(a.Num, b.Num));
        }

        private Val ParseAdd()
        {
            var a = ParseMul();
            while (true)
            {
                if (Take("+"))
                {
                    var b = ParseMul();
                    if (a.Kind == 2 || b.Kind == 2)
                    {
                        if (a.Kind == 0 || b.Kind == 0) a = Val.Unknown;
                        else
                        {
                            string asr = a.Kind == 2 ? a.Str : a.Kind == 1 ? a.Num.ToString(CultureInfo.InvariantCulture) : "";
                            string bsr = b.Kind == 2 ? b.Str : b.Kind == 1 ? b.Num.ToString(CultureInfo.InvariantCulture) : "";
                            a = Val.S(asr + bsr);
                        }
                    }
                    else if (a.Kind == 1 && b.Kind == 1) a = Val.N(a.Num + b.Num);
                    else a = Val.Unknown;
                }
                else if (Take("-"))
                {
                    var b = ParseMul();
                    a = a.Kind == 1 && b.Kind == 1 ? Val.N(a.Num - b.Num) : Val.Unknown;
                }
                else break;
            }
            return a;
        }

        private Val ParseMul()
        {
            var a = ParseUnary();
            while (true)
            {
                if (Take("*"))
                {
                    var b = ParseUnary();
                    a = a.Kind == 1 && b.Kind == 1 ? Val.N(a.Num * b.Num) : Val.Unknown;
                }
                else if (Take("/"))
                {
                    var b = ParseUnary();
                    a = a.Kind == 1 && b.Kind == 1 && Math.Abs(b.Num) > 1e-9 ? Val.N(a.Num / b.Num) : Val.Unknown;
                }
                else if (Take("%") || Take(" mod "))
                {
                    var b = ParseUnary();
                    a = a.Kind == 1 && b.Kind == 1 && Math.Abs(b.Num) > 1e-9 ? Val.N(a.Num % b.Num) : Val.Unknown;
                }
                else break;
            }
            return a;
        }

        private Val ParseUnary()
        {
            if (Take("!")) return Val.T(Not(ParseUnary().AsTruth()));
            if (Take("-")) { var v = ParseUnary(); return v.Kind == 1 ? Val.N(-v.Num) : Val.Unknown; }
            if (Take("+")) return ParseUnary();
            return ParsePrimary();
        }

        private Val ParsePrimary()
        {
            Ws();
            if (_p >= _s.Length) { Ok = false; return Val.Unknown; }
            char c = _s[_p];
            if (c == '(')
            {
                _p++;
                var v = ParseTernary();
                if (!Take(")")) Ok = false;
                return v;
            }
            if (c == '"')
            {
                _p++;
                var sb = new StringBuilder();
                while (_p < _s.Length && _s[_p] != '"')
                {
                    if (_s[_p] == '\\' && _p + 1 < _s.Length)
                    {
                        _p++;
                        sb.Append(_s[_p] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', var ch => ch });
                        _p++;
                    }
                    else sb.Append(_s[_p++]);
                }
                if (_p < _s.Length) _p++;
                return Val.S(sb.ToString());
            }
            if (char.IsDigit(c) || (c == '.' && _p + 1 < _s.Length && char.IsDigit(_s[_p + 1])))
            {
                int start = _p;
                while (_p < _s.Length && (char.IsDigit(_s[_p]) || _s[_p] == '.')) _p++;
                var txt = _s[start.._p];
                return double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? Val.N(n) : Val.Unknown;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = _p;
                while (_p < _s.Length && (char.IsLetterOrDigit(_s[_p]) || _s[_p] == '_' || _s[_p] == '.')) _p++;
                string ident = _s[start.._p];
                Ws();
                if (_p < _s.Length && _s[_p] == '(')
                {
                    // 函数调用：跳过参数，未知
                    int bal = 0;
                    do
                    {
                        if (_s[_p] == '(') bal++;
                        else if (_s[_p] == ')') bal--;
                        _p++;
                    } while (_p < _s.Length && bal > 0);
                    return Val.Unknown;
                }
                if (_p < _s.Length && _s[_p] == '[')
                {
                    int bal = 0;
                    do
                    {
                        if (_s[_p] == '[') bal++;
                        else if (_s[_p] == ']') bal--;
                        _p++;
                    } while (_p < _s.Length && bal > 0);
                    return Val.Unknown;
                }
                switch (ident)
                {
                    case "true": return Val.B(true);
                    case "false": return Val.B(false);
                    case "room": return _room >= 0 ? Val.N(_room) : Val.Unknown;
                    case "x":
                    case "self.x": return (_inWith || !_hasX) ? Val.Unknown : Val.N(_x);
                    case "y":
                    case "self.y": return (_inWith || !_hasY) ? Val.Unknown : Val.N(_y);
                }
                return Val.Unknown;
            }
            Ok = false;
            return Val.Unknown;
        }
    }

    // ───────────────────────── lang 文件 ─────────────────────────

    private void EnsureLang()
    {
        if (_langTried) return;
        _langTried = true;
        try
        {
            foreach (var candidate in LangCandidates())
            {
                if (!File.Exists(candidate)) continue;
                var doc = JsonDocument.Parse(File.ReadAllBytes(candidate), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String) map[prop.Name] = prop.Value.GetString() ?? "";
                }
                if (map.Count == 0) continue;
                _lang = map;
                _langPath = candidate;
                return;
            }
        }
        catch (Exception ex)
        {
            _decompileError ??= "lang 解析失败: " + ex.Message;
        }
    }

    /// <summary>
    /// lang 文件查找顺序（副本也能用）：
    /// ① data.win 同目录及最多 4 层祖先目录下的 lang/lang_{zh,en,ja}.json；
    /// ② 从路径/文件名推断章节（chapterN / chN），回退到游戏根 chapterN_windows/lang；
    /// ③ 打开的是 backup/chapterN_windows 基线时，回退到同章 live 目录。
    /// </summary>
    private IEnumerable<string> LangCandidates() => LangCandidatesFor(_dataWinPath);

    /// <summary>
    /// lang 文件查找顺序的静态版（供自检/CLI 在打开 data.win 前预测；不解析内容）：
    /// ① data.win 同目录及最多 4 层祖先目录下的 lang/lang_{zh,en,ja}.json；
    /// ② 从路径/文件名推断章节（chapterN / chN），回退到游戏根 chapterN_windows/lang；
    /// ③ 打开的是 backup/chapterN_windows 基线时，回退到同章 live 目录。
    /// </summary>
    public static List<string> LangCandidatesFor(string dataWinPath)
    {
        var list = new List<string>();
        foreach (var p in LangCandidatesCore(dataWinPath)) list.Add(p);
        return list;
    }

    /// <summary>只报告「会不会找到 lang」的文案（与 LangStatus 同口径；不解析内容），用于有/无 lang 两种 fixture 自检。</summary>
    public static string LangStatusFor(string dataWinPath)
    {
        foreach (var p in LangCandidatesFor(dataWinPath))
            if (File.Exists(p)) return "文本来源: " + p;
        return "未找到 lang/lang_en.json（只显示 lang key）";
    }

    private static IEnumerable<string> LangCandidatesCore(string dataWinPath)
    {
        var full = Path.GetFullPath(dataWinPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            foreach (var name in new[] { "lang_zh.json", "lang_en.json", "lang_ja.json" })
            {
                var p = Path.Combine(d.FullName, "lang", name);
                if (seen.Add(p)) yield return p;
            }
            if (d.FullName.Length == 0 || d.Parent == null) break;
        }
        int chap = GuessChapter(full);
        string? root = null;
        try { root = FileRole.FindGameRoot(); } catch { }
        if (root != null && chap > 0)
        {
            var chapterDir = "chapter" + chap + "_windows";
            foreach (var name in new[] { "lang_zh.json", "lang_en.json", "lang_ja.json" })
            {
                var p = Path.Combine(root, chapterDir, "lang", name);
                if (seen.Add(p)) yield return p;
            }
            foreach (var name in new[] { "lang_zh.json", "lang_en.json", "lang_ja.json" })
            {
                var p = Path.Combine(root, "backup", chapterDir, "lang", name);
                if (seen.Add(p)) yield return p;
            }
        }
    }

    /// <summary>从 data.win 路径/文件名推断章节号（chapter2_windows、ch3-edit、backup/chapter5…）；0=未知。</summary>
    public static int GuessChapter(string path)
    {
        var m = Regex.Match(path, @"chapter[ _-]?(\d)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n >= 1 && n <= 9) return n;
        var name = Path.GetFileNameWithoutExtension(path);
        m = Regex.Match(name, @"(?:^|[^a-z])ch[ _-]?(\d)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n >= 1 && n <= 9) return n;
        return 0;
    }

    public string ResolveKey(string key)
    {
        EnsureLang();
        if (_lang != null && _lang.TryGetValue(key, out var s)) return Clean(s);
        return "";
    }

    // ───────────────────────── 文本工具 ─────────────────────────

    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s;
        t = Regex.Replace(t, @"\\[Mm]\d+", "");          // \M1 / \m2 样式
        t = Regex.Replace(t, @"\\E\d+", "");             // \E4 表情/音效控制
        t = Regex.Replace(t, @"\\e\d+", "");
        t = Regex.Replace(t, @"\\c[A-Za-z]", "");         // \cY 颜色
        t = Regex.Replace(t, @"~\d+", "");                 // ~1 颜色/停顿
        t = Regex.Replace(t, @"\\[A-Za-z]\d*", "");         // 其余 GML 文本控制码（\C2 / \F0 / \T1 …）
        t = Regex.Replace(t, @"\^\d+", "");
        t = t.Replace("%%", "%");
        t = t.Replace("\r\n", "\n").Replace('\r', '\n');
        t = t.Replace("\n", " / ");
        t = Regex.Replace(t, "[ \t]+", " ");
        return t.Trim();
    }

    private static string UnescapeGml(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                char esc = s[i];
                if (esc == 'n') sb.Append('\n');
                else if (esc == 'r') sb.Append('\r');
                else if (esc == 't') sb.Append('\t');
                else if (esc == '\\' || esc == '"') sb.Append(esc);
                // 未知转义（典型是文本控制码 \C2 / \M1 / \E4）：保留反斜杠，交给 Clean() 统一剥掉，
                // 否则反斜杠会被吞掉、只剩 "C2" 被当成正文（独立审计的残留问题）。
                else { sb.Append('\\'); sb.Append(esc); }
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string FriendlyEventName(string type, int subtype)
    {
        string friendly = type switch
        {
            "Create" => "创建",
            "Destroy" => "销毁",
            "Alarm" => "闹钟",
            "Step" => "每帧",
            "Draw" => "绘制",
            "Other" => "用户事件",
            "KeyPress" => "按键按下",
            "Collision" => "碰撞",
            "PreCreate" => "预创建",
            _ => type,
        };
        string extra = type == "Other" ? (subtype == 10 ? " · 交互" : " · 非交互事件") : "";
        return type + "_" + subtype + "（" + friendly + extra + "）";
    }

    private static string ExtractParen(string s, int openIdx)
    {
        if (openIdx < 0) return "";
        int depth = 0;
        var sb = new StringBuilder();
        for (int i = openIdx; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(') { depth++; if (depth == 1) continue; }
            else if (c == ')') { depth--; if (depth == 0) break; }
            if (depth >= 1) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>把字符串字面量替换成空，便于括号/花括号计数与关键字判断。</summary>
    private static string StripStrings(string s)
    {
        if (!s.Contains('"')) return s;
        var sb = new StringBuilder(s.Length);
        bool inStr = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                if (c == '\\' && i + 1 < s.Length) i++;
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

internal static class ValExtensions
{
    public static T Let<T>(this T self, Func<T, T> f) => f(self);
}