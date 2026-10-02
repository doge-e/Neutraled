using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>把 Neutraled 核心（api 脚本 + 控制器对象 + 引导 + mod 清单）注入 data.win。</summary>
public static class Injector
{
    /// <summary>--no-cache：禁读输入扫描缓存（计时用）。</summary>
    public static bool NoCache = false;

    /// <summary>
    /// Neutraled 自己会用 find/replace 改写（ApplyGamePatches / ApplyBuiltinHooks）的代码对象。
    /// ★ 源码级差异层（--layer-from-base）如果把整个脚本覆盖上去，会连 Neutraled 的注入点一起冲掉：
    ///   实测 60 FPS 层覆盖 gml_Object_obj_darkcontroller_Draw_0 / _Step_0 之后，设置菜单里
    ///   「Mod 设置」行和「Return to Title」被挤到同一个 y（395 处堆了 4 行），键盘分派也错位。
    ///   ⇒ LayerFromBase 会自动把命中的目标写进 mod.json 的 patch_skip；这里再兜底告警。
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedPatchTargets = new[]
    {
        "gml_Object_obj_custommenu_Create_0",    // 菜单竖直/横向循环
        "gml_Object_obj_savemenu_Draw_0",        // 存档菜单循环
        "gml_Object_obj_darkcontroller_Step_0",  // 设置菜单行数/取模/按键分派
        "gml_Object_obj_darkcontroller_Draw_0",  // 设置菜单「Mod 设置」行 + 面板绘制
        "gml_Object_obj_writer_Draw_0",          // 超长文本换行
        "gml_GlobalScript_scr_texttype",         // 打字机
        "gml_GlobalScript_snd_play",             // 音频钩子
        "gml_GlobalScript_snd_init",
        "gml_GlobalScript_mus_loop",
        "gml_GlobalScript_mus_play",
    };

    public static bool IsReservedPatchTarget(string codeName) =>
        ReservedPatchTargets.Contains(codeName, StringComparer.Ordinal);

    /// <summary>
    /// 把 objects 声明里的**按名引用**（精灵 / 父对象 / 遮罩）解析成实例。
    /// 三条规矩：
    ///   1) 引用找不到 → 响亮告警但**不**静默改成 -1：静默会让对象「存在但画不出来」，
    ///      正是「第一章传说全空白」那类事故的形态（症状在游戏里，线索全无）。
    ///   2) 只有**新建**的对象才写头部字段；已存在的对象只在声明与现状不符时提示，不改动
    ///      （对象头部的差异多半是索引/元数据位移，改它风险大于收益）。
    ///   3) 引用的是精灵时，层的 sprites/ 资源包（Injector 2.2 段）此时已导入，所以能解析到。
    /// </summary>
    private static void ApplyObjectRefs(UndertaleData data, string modId, ModObject od, UndertaleGameObject o, bool isNew)
    {
        if (!string.IsNullOrWhiteSpace(od.Sprite))
        {
            var sp = data.Sprites.ByName(od.Sprite!);
            if (sp == null)
                Paths.Log(L("    [警告] {0}: 对象 {1} 的精灵不存在: {2} ⇒ 对象会画不出来（该层需要带 sprites/ 资源包，或确认基底已有）",
                    modId, od.Name, od.Sprite!));
            else if (isNew) o.Sprite = sp;
            else if (!ReferenceEquals(o.Sprite, sp))
                Paths.Log(L("    [注意] {0}: 对象 {1} 已存在且精灵不同（声明 {2} / 现状 {3}），保持现状", modId, od.Name, od.Sprite!, o.Sprite?.Name?.Content ?? "(无)"));
        }
        if (!string.IsNullOrWhiteSpace(od.Parent))
        {
            var pa = data.GameObjects.ByName(od.Parent!);
            if (pa == null) Paths.Log(L("    [警告] {0}: 对象 {1} 的父对象不存在: {2}", modId, od.Name, od.Parent!));
            else if (isNew) o.ParentId = pa;
            else if (!ReferenceEquals(o.ParentId, pa))
                Paths.Log(L("    [注意] {0}: 对象 {1} 已存在且父对象不同（声明 {2} / 现状 {3}），保持现状", modId, od.Name, od.Parent!, o.ParentId?.Name?.Content ?? "(无)"));
        }
        if (!string.IsNullOrWhiteSpace(od.Mask))
        {
            var mk = data.Sprites.ByName(od.Mask!);
            if (mk == null) Paths.Log(L("    [警告] {0}: 对象 {1} 的遮罩精灵不存在: {2}", modId, od.Name, od.Mask!));
            else if (isNew) o.TextureMaskId = mk;
        }
    }

    public static UndertaleData Load(string path)
    {
        // ⚠ 不要"优化"这里的缓冲/FileOptions：实测把默认换成 4MB 缓冲 + SequentialScan
        //   会让 chapter5 部署从 47s 变 176s（慢 3.7 倍）。
        //   原因：UTMT 读写 data.win 时会**回填 seek**（补指针偏移），大缓冲在每次 seek 时都要刷盘。
        //   默认的 File.OpenRead / File.Create 就是这个场景下最快的选择。
        using var fs = File.OpenRead(path);
        var data = UndertaleIO.Read(fs, null, m => { });
        return data;
    }

    /// <summary>反编译一个代码条目为 GML 文本（用于精确匹配替换）。</summary>
    public static string Decompile(UndertaleData data, string codeName)
    {
        var code = data.Code.ByName(codeName) ?? throw new InvalidOperationException(L("找不到代码条目: ") + codeName);
        var gctx = new GlobalDecompileContext(data);
        var dctx = new Underanalyzer.Decompiler.DecompileContext(gctx, code, null!);
        return dctx.DecompileToString();
    }

    /// <summary>反编译（复用已建好的全局上下文）—— 批量时必须用这个。
    /// 重建 GlobalDecompileContext 会扫描全部资源，是最耗时的部分（约 3 秒/次）。</summary>
    public static string DecompileWith(GlobalDecompileContext gctx, UndertaleData data, string codeName)
    {
        var code = data.Code.ByName(codeName) ?? throw new InvalidOperationException(L("找不到代码条目: ") + codeName);
        var dctx = new Underanalyzer.Decompiler.DecompileContext(gctx, code, null!);
        return dctx.DecompileToString();
    }

    public static void Save(UndertaleData data, string path)
    {
        // ★ 落位用 **File.Move（同卷原子改名）而不是 File.Copy** ——
        //   Copy 会把整个 data.win 再抄一遍（chapter5 是 229MB 的额外读写 ≈ 10 秒）。
        var tmp = path + ".ntl-tmp";
        // ⚠ 两个实测过的坑：① Flush(true) = fsync，229MB 多等 135 秒；
        //   ② 4MB 缓冲 + SequentialScan 反而慢 3.7 倍（回填 seek 场景）。保持默认。
        using (var fs = File.Create(tmp)) UndertaleIO.Write(fs, data);

        // ★ 落位重试（2026-09-26 实测）：MoveFileEx(REPLACE_EXISTING) 替换要求目标此刻
        //   没人打开、且不带只读属性。--deploy-all --force 并行部署 6 章时偶发 chapter1 失败：
        //     [错误] Access to the path is denied.
        //     at System.IO.FileSystem.MoveFile(String sourceFullPath, String destFullPath, Boolean overwrite)
        //     at Neutraled.Builder.Injector.Save(...) in Injector.cs:line 52
        //   杀软 / Steam 云同步 / 游戏进程短暂持有 data.win 就会这样；同一命令重跑即 6/6 成功
        //   （偶发，非稳定复现）。这里加短退避重试（上限 8 次、累计约 4 秒），并在重试前顺手
        //   清掉只读属性；非 IO/权限类异常与最后一次失败照旧抛出（错误信息不变）。
        const int attempts = 8;
        for (int i = 0; ; i++)
        {
            try
            {
                File.Move(tmp, path, true);
                return;
            }
            catch (Exception ex) when (i < attempts - 1 && (ex is IOException || ex is UnauthorizedAccessException))
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.IsReadOnly) fi.IsReadOnly = false;
                }
                catch { }
                Paths.Log(L("    写入 data.win 被占用，重试 {0}/{1}…（{2}: {3}）", i + 2, attempts, ex.GetType().Name, ex.Message));
                System.Threading.Thread.Sleep(120 * (i + 1));
            }
        }
    }

    /// <summary>核心注入主流程。</summary>
    public static void InjectCore(UndertaleData data, string apiDir, List<ModEntry> mods, string bootCodeName,
        HashSet<string>? baseModified = null, string? gameRoot = null, string? chapter = null)
    {
        var ctx = new GlobalDecompileContext(data);
        IDecompileSettings? settings = null;
        var group = new CodeImportGroup(data, ctx, settings!);

        // ---------- 0) 预扫描：分配 mod 脚本名（文件名优先，冲突加前缀） ----------
        var usedNames = new HashSet<string>(data.Scripts.Select(s => s.Name?.Content ?? ""));
        var plan = new List<(string file, string scriptName, bool isMain, ModEntry mod, string source)>();
        int unwrappedFiles = 0, unwrappedFuncs = 0;
        var modMain = new Dictionary<string, string>();
        foreach (var m in mods)
        {
            var gmlDir0 = Path.Combine(m.Dir, "gml");
            if (!Directory.Exists(gmlDir0)) continue;
            var safeId0 = Sanitize(m.Id);
            string? mainName = null;
            foreach (var f0 in Directory.GetFiles(gmlDir0, "*.gml").OrderBy(x => x))
            {
                var baseName0 = Path.GetFileNameWithoutExtension(f0);
                // ★ 新脚本剥壳：把 function 名(参数) { ... } 拆成「脚本名 = 函数名」的裸函数体脚本。
                //   原样注入会走 UTMT 的函数定义路径（另建一个空壳脚本承载函数绑定，运行期按名字
                //   调用返回 0）—— 实测 dojo 的 dj_path 就是这样让 chapter1 在 scr_84_init_localization
                //   里 ini_open(0) 静默失败、随后 ini_read_* 报 undefined INI 的。
                var pieces = ScriptUnwrap.Split(baseName0, File.ReadAllText(f0), out var unote);
                if (unote.Length > 0)
                {
                    unwrappedFiles++;
                    unwrappedFuncs += pieces.Count;
                    // 单函数且函数名 == 文件名的（绝大多数）不逐个刷屏，只在汇总里计数
                    if (pieces.Count > 1 || !string.Equals(pieces[0].name, baseName0, StringComparison.Ordinal))
                        Paths.Log(L("    [脚本剥壳] {0}: {1} -> {2}", Path.GetFileName(f0), unote,
                            string.Join(", ", pieces.Select(p => p.name))));
                }
                foreach (var (srcName, srcText) in pieces)
                {
                    var scriptName0 = srcName;
                    if (usedNames.Contains(scriptName0))
                    {
                        scriptName0 = $"ntl_{safeId0}_{srcName}";
                        Paths.Log(L("    [警告] 脚本名冲突 {0} -> {1}", srcName, scriptName0));
                    }
                    usedNames.Add(scriptName0);
                    // ★ 入口判定必须看「源码名」，不能看最终脚本资源名：多个 mod 都有 gml/main.gml 时，
                    //   只有第一个能叫 main，后面的会被改名成 ntl_<id>_main；若按改名后的名字判定，
                    //   它们的 entry 会被记成空 ⇒ 那些 mod 的入口永远不跑（实测：chapter1 里 3 个有 main
                    //   的 mod 只有 1 个跑了）。
                    bool isMain0 = string.Equals(srcName, "main", StringComparison.OrdinalIgnoreCase);
                    plan.Add((f0, scriptName0, isMain0, m, srcText));
                    if (isMain0) mainName = scriptName0;
                }
            }
            modMain[m.Id] = mainName ?? "";
        }

        // ---------- 1) api 脚本（每个 .gml = 一个同名脚本资源） ----------
        var scriptFiles = Directory.GetFiles(apiDir, "*.gml").OrderBy(f => f).ToList();
        int scriptCount = 0;
        foreach (var f in scriptFiles)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var content = File.ReadAllText(f);

            // 引导脚本：内联 mod 清单
            if (name == "scr_ntl_init")
                content = content.Replace("//__NTL_MANIFEST__", BuildManifest(mods, modMain));

            var codeName = "gml_Script_" + name;
            var code = UndertaleCode.CreateEmptyEntry(data, codeName);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
            group.QueueReplace(code, content);
            scriptCount++;
        }
        Paths.Log(L("  api 脚本: {0} 个", scriptCount));

        // ---------- 2) mod 脚本（按预扫描的名字编译） ----------
        int modScripts = 0;
        foreach (var (file, scriptName, isMain, mod, source) in plan)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + scriptName);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(scriptName), Code = code });
            group.QueueReplace(code, source);
            modScripts++;
        }
        Paths.Log(L("  mod 脚本: {0} 个（入口: {1}）", modScripts, string.Join(", ", modMain.Values.Where(v => v != ""))));
        if (unwrappedFiles > 0) Paths.Log(L("  脚本剥壳: {0} 个文件 / {1} 条脚本（函数定义 -> 裸函数体；只有拆分/改名的逐个列出）", unwrappedFiles, unwrappedFuncs));

        // ---------- 2.2) 资源包导入（sprites/*.json + PNG） ----------
        int spriteTotal = 0;
        foreach (var m in mods)
        {
            var spDir = Path.Combine(m.Dir, "sprites");
            if (!Directory.Exists(spDir)) continue;
            Paths.Log(L("  精灵资源包: {0}", m.Id));
            spriteTotal += SpriteImport.Import(data, spDir);
        }
        if (spriteTotal > 0) Paths.Log(L("  精灵导入合计: {0}", spriteTotal));

        // ---------- 2.3) 声音资源包导入（sounds/*.json + 音频） ----------
        foreach (var m in mods)
        {
            var sndDir = Path.Combine(m.Dir, "sounds");
            if (!Directory.Exists(sndDir)) continue;
            Paths.Log(L("  声音资源包: {0}", m.Id));
            SoundImport.Import(data, sndDir);
        }

        // ---------- 2.4) 字体资源包导入（fonts/*.json + 字形 PNG） ----------
        foreach (var m in mods)
        {
            var fontDir = Path.Combine(m.Dir, "fonts");
            if (!Directory.Exists(fontDir)) continue;
            Paths.Log(L("  字体资源包: {0}", m.Id));
            FontImport.Import(data, fontDir);
        }

        // ---------- 2.41) Neutraled 自带字体包（中文等）—— 与 mod 无关，始终导入 ----------
        //   来源：Neutraled/fonts/*.json（用 --make-cjk-font 生成）
        var ntlFontDir = Path.Combine(Path.GetDirectoryName(apiDir) ?? ".", "fonts");
        if (Directory.Exists(ntlFontDir))
        {
            // ★ 部署期「本机字形覆盖」：把用户自己 data.win 里的 8bitoperator JVE（拉丁）
            //   与汉化像素汉字覆盖进 OFL 包的**副本** —— 发布包只带 OFL 字形，观感却与游戏一致。
            //   （fonts/ntl_native_sources.json 声明区间；失败自动回退纯 OFL 包，绝不中断部署）
            var ntlGameRoot = gameRoot ?? Path.GetDirectoryName(Path.GetDirectoryName(apiDir) ?? ".") ?? ".";
            var ntlUseDir = FontNative.Prepare(data, ntlGameRoot, ntlFontDir, chapter ?? "root") ?? ntlFontDir;
            var ntlFonts = FontImport.Import(data, ntlUseDir);
            if (ntlFonts > 0) Paths.Log(L("    内置字体包: {0} 个（中文等）", ntlFonts));
        }

        PhaseTimer.Mark("0-2.41 预扫描 + api/mod 脚本 + 精灵/音效/字体包");

        // ---------- 2\.42\) 着色器资源包导入（shaders/*.json） ----------
        //   ★ 层只带代码、不带资源：精灵/声音/字体走 --export-packs，着色器原来没有通道，
        //     于是层里 asset_get_index("sh_color_filter") 永远拿不到东西（运行期静默 -1，
        //     滤镜整个失效且日志一个字都没有）。--export-shaders 导出、这里导入。
        int shaderTotal = 0;
        foreach (var m in mods)
        {
            var shDir = Path.Combine(m.Dir, ShaderPack.DirName);
            if (!Directory.Exists(shDir)) continue;
            Paths.Log(L("  着色器资源包: {0}", m.Id));
            shaderTotal += ShaderPack.Import(data, shDir);
        }
        if (shaderTotal > 0) Paths.Log(L("  着色器导入合计: {0}", shaderTotal));

        PhaseTimer.Mark("2.42 着色器资源包");

        // ---------- 2\.45\) references 字节码复制（从 mod 的 ref/data.win 复制代码对象） ----------
        var refCopiedTargets = new HashSet<string>(StringComparer.Ordinal);
        var refSources = new List<UndertaleData>();
        foreach (var m in mods)
        {
            var codes = m.References?.Codes;
            if (codes == null || codes.Count == 0) continue;
            if (string.IsNullOrEmpty(m.RefSource)) { Paths.Log(L("    [警告] {0} 声明了 codes 但缺少 references.source", m.Id)); continue; }
            var refPath = Path.Combine(m.Dir, m.RefSource!);
            if (!File.Exists(refPath)) { Paths.Log(L("    [警告] 引用源缺失: {0}", refPath)); continue; }

            var overrides = new HashSet<string>(m.References!.Override, StringComparer.Ordinal);

            // mod-vs-mod 去重：同一目标已被前序 mod 复制且本 mod 未声明 override → 跳过
            var toCopy = new List<string>();
            foreach (var c in codes.Distinct())
            {
                if (refCopiedTargets.Contains(c) && !overrides.Contains(c))
                {
                    Paths.Log(L("    [冲突] {0} 已被前序 mod 复制，{1} 跳过（如需覆盖请声明 references.override）", c, m.Id));
                    continue;
                }
                toCopy.Add(c);
            }
            if (toCopy.Count == 0) continue;

            Paths.Log(L("  引用复制: {0} <- {1}（{2} 个代码对象）", m.Id, m.RefSource, toCopy.Count));
            var srcData = Injector.Load(refPath);
            refSources.Add(srcData);
            RefCopy.Copy(data, srcData, toCopy, overrides, baseModified);
            foreach (var c in toCopy) refCopiedTargets.Add(c);
        }

        PhaseTimer.Mark("2.45 references 复制");

        // ---------- 2\.5\) mod patches（整脚本覆盖 / 局部文本替换） ----------
        //   基底不是原版时（整包 mod 当基底）：patch 会把基底对同一对象的改动一起冲掉
        //   ⇒ 与官方基线做**三方合并**（base=官方 / ours=基底 / theirs=patch），冲突保守取基底。
        int patchCount = 0, frCount = 0, skipCount = 0;
        //   ★ 兜底：层盖到 Neutraled 自己的注入目标上（ReservedPatchTargets）也要三方合并 ——
        //     否则内置注入点（设置菜单「Mod 设置」行 / 打字机 / 音频钩子）会被整脚本覆盖冲掉。
        var reservedTouched = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m0 in mods)
            foreach (var p0 in m0.Patches)
                if (IsReservedPatchTarget(p0.Target)) reservedTouched.Add(p0.Target);
        //   ★ 兜底 2：层 vs 层 —— 多个 mod 都 patch 同一个目标时，后一个的整脚本覆盖会**静默吃掉**前一个的改动。
        //     实例（2026-09-27，chapter1）：DEVICE_MENU_Step_0 被 60fps 层 + dojo 层 + pct 层三方争用，
        //     dojo 的 dj_open() 入口（道场菜单入口）被 pct 的版本整脚本覆盖 ⇒ 按 C 进不去道场。
        //     争用目标同样纳入三方合并（base=官方基线 / ours=已应用的前序层 / theirs=本层 patch）。
        var patchOwners = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var m0 in mods)
            foreach (var p0 in m0.Patches)
            {
                patchOwners.TryGetValue(p0.Target, out var ownerCount);
                patchOwners[p0.Target] = ownerCount + 1;
            }
        var contestedTargets = new System.Collections.Generic.List<string>();
        foreach (var kv in patchOwners)
            if (kv.Value > 1 && !reservedTouched.Contains(kv.Key)) contestedTargets.Add(kv.Key);
        contestedTargets.Sort(System.StringComparer.Ordinal);
        foreach (var t2 in contestedTargets) reservedTouched.Add(t2);
        var merger = (gameRoot != null && chapter != null)
            ? BasePatchMerger.TryCreate(data, gameRoot, chapter, baseModified, reservedTouched)
            : null;
        if (merger != null) Paths.Log(L("  三方合并: 已启用（基底 ≠ 官方基线，patch 目标重叠时合并）"));
        if (contestedTargets.Count > 0)
        {
            if (merger != null)
            {
                Paths.Log(L("  层间争用: {0} 个对象被多个层 patch，已全部改用三方合并", contestedTargets.Count));
                foreach (var t2 in contestedTargets.Take(10))
                    Paths.Log(L("    [合并] {0} 个层都改 {1}，改用三方合并（避免后一个层整脚本覆盖前一个）", patchOwners[t2], t2));
                if (contestedTargets.Count > 10)
                    Paths.Log(L("    …另有 {0} 个争用对象（未逐条列出）", contestedTargets.Count - 10));
            }
            else
            {
                foreach (var t2 in contestedTargets.Take(10))
                    Paths.Log(L("    [警告] {0} 个层都改 {1}，后一个会整脚本覆盖前一个（缺官方基线备份，无法三方合并）", patchOwners[t2], t2));
            }
        }
        foreach (var m in mods)
        {
            foreach (var p in m.Patches)
            {
                if (m.PatchSkip.Contains(p.Target)) { Paths.Log(L("    [跳过] {0}: {1} (patch_skip)", m.Id, p.Target)); skipCount++; continue; }
                // ★ 兜底告警：整脚本覆盖 Neutraled 自己的注入目标 ⇒ 内置补丁会被冲掉（静默失效重灾区）。
                if (IsReservedPatchTarget(p.Target))
                {
                    if (merger != null)
                        Paths.Log(L("    [合并] {0}: {1} 是 Neutraled 内置补丁目标，已改用三方合并（内置注入点保留）", m.Id, p.Target));
                    else
                        Paths.Log(L("    [警告] {0}: {1} 是 Neutraled 内置补丁目标，整脚本覆盖会冲掉内置注入点（应改用 patch_skip）", m.Id, p.Target));
                }
                var file = Path.Combine(m.Dir, p.File);
                if (!File.Exists(file)) { Paths.Log(L("    [警告] patch 文件缺失: {0}", file)); continue; }
                if (data.Code.ByName(p.Target) == null) { Paths.Log(L("    [警告] patch 目标不存在: {0}", p.Target)); continue; }
                var patchSrc = File.ReadAllText(file);
                if (merger != null) patchSrc = merger.Merge(p.Target, patchSrc);
                group.QueueReplace(p.Target, patchSrc);
                patchCount++;
            }
            foreach (var f in m.FindReplace)
            {
                if (data.Code.ByName(f.Target) == null) { Paths.Log(L("    [警告] find_replace 目标不存在: {0}", f.Target)); continue; }
                group.QueueFindReplace(f.Target, f.Find, f.Replace);
                frCount++;
            }
        }
        Paths.Log(L("  mod patches: {0} 覆盖 / {1} 局部替换 / {2} 跳过", patchCount, frCount, skipCount));
        merger?.WriteSummary(Paths.NeutraledRoot(gameRoot!), chapter!);

        PhaseTimer.Mark("2.5 patches + 三方合并");

        // ---------- 2\.6\) 新增对象 + 事件（mod.json 的 objects 段） ----------
        //   ★ 为什么需要本节：靠**新对象**当入口的 mod（DOJO 的 obj_dojo / obj_dojo_act_projectile /
        //     obj_dojo_arena_back / obj_dojo_back，percentage_color 的若干新资源）以前表达不成层 ——
        //     patch 要求目标已存在、gml/ 只能加函数脚本，于是一律进「无法表达」，只能整包当基底；
        //     而基底位一次只有一个（本机 6 个整包 mod 抢一个位）。
        //     现在按声明在 data.win 里真正新建 UndertaleGameObject 并挂事件代码：
        //     --layer-from-base 产出的层与整包 mod 用**同一段声明**，于是新对象 mod 也能叠加。
        //   与其它段的顺序：2.2 精灵包 → 2.6（这里引用的精灵此时已存在）→ 3)/4) 编译。
        int objNew = 0, objReuse = 0, objEvt = 0, objBad = 0;
        foreach (var m in mods)
        {
            if (m.Objects.Count == 0) continue;
            Paths.Log(L("  新增对象: {0}（{1} 条声明）", m.Id, m.Objects.Count));
            foreach (var od in m.Objects)
            {
                if (string.IsNullOrWhiteSpace(od.Name))
                {
                    Paths.Log(L("    [警告] {0}: objects 条目缺 name，跳过", m.Id));
                    objBad++;
                    continue;
                }
                var o = data.GameObjects.ByName(od.Name);
                bool isNew = o == null;
                if (isNew)
                {
                    o = new UndertaleGameObject
                    {
                        Name = data.Strings.MakeString(od.Name),
                        Visible = od.Visible,
                        Solid = od.Solid,
                        Persistent = od.Persistent,
                        Depth = od.Depth ?? 0,
                        Managed = true,
                    };
                    data.GameObjects.Add(o);
                    objNew++;
                    Paths.Log(L("    +对象 {0}（visible={1} solid={2} persistent={3} depth={4}）",
                        od.Name, od.Visible, od.Solid, od.Persistent, od.Depth ?? 0));
                }
                else objReuse++;

                ApplyObjectRefs(data, m.Id, od, o, isNew);

                foreach (var e in od.Events)
                {
                    if (!ObjectEvents.TryParse(e.Type, out var evtType))
                    {
                        Paths.Log(L("    [警告] {0}: 事件类型不认识: {1}（对象 {2}）；支持: {3}", m.Id, e.Type, od.Name, ObjectEvents.Supported));
                        objBad++;
                        continue;
                    }
                    var codeName = ObjectEvents.CodeName(od.Name, e.Type, e.Subtype);
                    if (m.PatchSkip.Contains(codeName)) { Paths.Log(L("    [跳过] {0}: {1} (patch_skip)", m.Id, codeName)); continue; }
                    var file = Path.Combine(m.Dir, e.File);
                    if (!File.Exists(file)) { Paths.Log(L("    [警告] {0}: 事件源文件缺失: {1}", m.Id, file)); objBad++; continue; }

                    // 已存在同名代码条目（例如既有对象补一个事件、或两个 mod 都声明了同一事件）：
                    // 复用条目本身，LinkEvent 会把它挂到该对象的事件表上（同类型同子类型不会重复挂）。
                    var code = data.Code.ByName(codeName);
                    if (code == null) code = UndertaleCode.CreateEmptyEntry(data, codeName);
                    else Paths.Log(L("    [注意] 事件代码条目已存在，复用: {0}", codeName));
                    group.QueueReplace(code, File.ReadAllText(file));
                    CodeImportGroup.LinkEvent(o, code, evtType, (uint)e.Subtype);
                    objEvt++;
                    Paths.Log(L("    事件 {0}（subtype {1}）← {2}", e.Type, e.Subtype, e.File));
                }
            }
        }
        if (objNew + objReuse > 0 || objEvt > 0)
            Paths.Log(L("  新增对象合计: 新建 {0} / 已存在 {1} / 事件 {2} / 跳过 {3}", objNew, objReuse, objEvt, objBad));

        PhaseTimer.Mark("2.6 新增对象 + 事件");

        // ---------- 2\.7\) 新增房间（mod.json 的 rooms 段） ----------
        //   ★ 为什么需要本节：房间不属于任何代码条目 —— patch（要目标已存在）、gml/（只能加函数脚本）、
        //     objects 段（只管对象事件）三段都碰不到它，而靠 room_goto(N) 进自己房间的 mod
        //     （dojo：dj_open() 里 room_goto(147)）少了这个房间，入口在第一跳就断掉。
        //   索引 = **RoomOrder 的下标**：把房间追加到 data.Rooms 与 RoomOrder 的末尾，新房间的编号
        //   正好等于原房间数（vanilla ch1 有 147 个 ⇒ room_dojo 拿到 147 ✓ 与 dojo 源里的硬编码一致）。
        //   只建**空房间**：入口型 mod 的房间就是一块画布（room_dojo 640x480，实例/图块/层全 0，
        //   场地全靠 obj_dojo 自己的 Draw）；带实例/图块/层的房间需要完整序列化，层不给，由转换报告指出。
        //   GMS2 房间的 8 个默认视图 / 8 个默认背景由 new UndertaleRoom() 自带，只需覆盖启用视图的字段。
        int roomNew = 0, roomSkip = 0, roomBad2 = 0;
        foreach (var m in mods)
        {
            if (m.Rooms.Count == 0) continue;
            Paths.Log(L("  新增房间: {0}（{1} 条声明）", m.Id, m.Rooms.Count));
            foreach (var rd in m.Rooms)
            {
                if (string.IsNullOrWhiteSpace(rd.Name))
                {
                    Paths.Log(L("    [警告] {0}: rooms 条目缺 name，跳过", m.Id));
                    roomBad2++;
                    continue;
                }
                if (data.Rooms.Any(r => string.Equals(r.Name?.Content, rd.Name, StringComparison.Ordinal)))
                {
                    roomSkip++;
                    Paths.Log(L("    [跳过] 房间已存在: {0}", rd.Name));
                    continue;
                }
                var room = new UndertaleRoom
                {
                    Name = data.Strings.MakeString(rd.Name),
                    Width = rd.Width,
                    Height = rd.Height,
                    Speed = rd.Speed,
                    Persistent = rd.Persistent,
                    Flags = (UndertaleRoom.RoomEntryFlags)rd.Flags,
                    BackgroundColor = unchecked((uint)rd.BackgroundColor),
                    DrawBackgroundColor = rd.DrawBackgroundColor,
                };
                foreach (var rv in rd.Views)
                {
                    if (room.Views == null || rv.Index < 0 || rv.Index >= room.Views.Count)
                    {
                        Paths.Log(L("    [警告] {0}: 视图下标越界，忽略: {1}", m.Id, rv.Index));
                        roomBad2++;
                        continue;
                    }
                    var v = room.Views[rv.Index];
                    v.Enabled = true;
                    v.PortX = rv.PortX; v.PortY = rv.PortY; v.PortWidth = rv.PortWidth; v.PortHeight = rv.PortHeight;
                    v.ViewX = rv.ViewX; v.ViewY = rv.ViewY; v.ViewWidth = rv.ViewWidth; v.ViewHeight = rv.ViewHeight;
                    v.SpeedX = rv.SpeedX; v.SpeedY = rv.SpeedY;
                    v.BorderX = rv.BorderX; v.BorderY = rv.BorderY;
                    if (!string.IsNullOrWhiteSpace(rv.Object))
                    {
                        var vo = data.GameObjects.ByName(rv.Object!);
                        if (vo == null) Paths.Log(L("    [警告] {0}: 视图跟随对象不存在: {1}", m.Id, rv.Object));
                        else v.ObjectId = vo;
                    }
                }
                data.Rooms.Add(room);
                data.GeneralInfo.RoomOrder.Add(new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>(room));
                roomNew++;
                Paths.Log(L("    +房间 {0}（{1}x{2} 速度 {3} 标志 {4}）⇒ room_goto 索引 {5}",
                    rd.Name, rd.Width, rd.Height, rd.Speed, rd.Flags, data.Rooms.Count - 1));
            }
        }
        if (roomNew + roomSkip > 0)
            Paths.Log(L("  新增房间合计: 新建 {0} / 已存在 {1} / 异常 {2}", roomNew, roomSkip, roomBad2));

        PhaseTimer.Mark("2.7 新增房间");

        // ---------- 2\.8\) 资源名守卫（只告警，不中止） ----------
        // 到这里产物的资源集合已经定型（精灵包 / references 复制 / patches / 新增对象 / 新增房间都进完了），
        // 正适合拿它当「这个名字在不在」的基准：层只带代码不带资源，字符串形式的引用运行期只会静默返回 -1。
        AssetNameGuard.Run(data, mods);

        PhaseTimer.Mark("2.8 资源名守卫");

        // ---------- 3\) 控制器事件代码 ----------
        var eventsDir = Path.Combine(apiDir, "events");
        var eventCodes = new Dictionary<string, UndertaleCode>();
        if (Directory.Exists(eventsDir))
        {
            foreach (var f in Directory.GetFiles(eventsDir, "*.gml").OrderBy(x => x))
            {
                var evt = Path.GetFileNameWithoutExtension(f);   // Create_0 / Step_1 / Draw_64
                var codeName = $"gml_Object_obj_ntl_core_{evt}";
                var code = UndertaleCode.CreateEmptyEntry(data, codeName);
                group.QueueReplace(code, File.ReadAllText(f));
                eventCodes[evt] = code;
            }
            Paths.Log(L("  事件代码: {0} 个", eventCodes.Count));
        }

        // ---------- 3.9) 函数 hook 包装 ----------
        try
        {
            var hookDecls = Hooks.Collect(mods);
            if (hookDecls.Count > 0)
            {
                Paths.Log(L("  函数 hook 声明: {0} 条", hookDecls.Count));
                Hooks.Wrap(data, hookDecls, group);
            }
        }
        catch (Exception ex) { Paths.Log(L("  [警告] hook 包装失败: {0}", ex.Message)); }

        // ---------- 4) 编译 ----------
        PhaseTimer.Mark("3+3.9 控制器事件 + 函数 hook 包装");
        var result = group.Import(true);
        if (!result.Successful)
        {
            Paths.Log(L("  [错误] 编译失败:"));
            Paths.Log(result.PrintAllErrors(true));
            throw new InvalidOperationException(L("Neutraled 核心编译失败"));
        }
        Paths.Log(L("  编译成功"));
        PhaseTimer.Mark("4 首次编译 (Import #1)");

        // ---------- 5) 控制器对象 ----------
        var coreName = "obj_ntl_core";
        var obj = data.GameObjects.ByName(coreName);
        if (obj == null)
        {
            obj = new UndertaleGameObject
            {
                Name = data.Strings.MakeString(coreName),
                Persistent = true,
                Depth = 100000,
                Visible = true,
                Solid = false,
                Managed = true
            };
            data.GameObjects.Add(obj);
            Paths.Log(L("  对象 {0} 创建", coreName));
        }

        foreach (var (evt, code) in eventCodes)
        {
            var parts = evt.Split('_');
            if (parts.Length < 2) continue;
            var typeName = parts[0];
            var subtype = uint.Parse(parts[1]);
            // 事件名 → EventType 走共用表（以前这里是第二份 switch，容易和 objects 段分叉）
            if (!ObjectEvents.TryParse(typeName, out var type))
            {
                Paths.Log(L("  [错误] 事件文件名的事件类型不认识: {0}（支持: {1}）", typeName, ObjectEvents.Supported));
                throw new InvalidOperationException(L("Neutraled 核心事件文件名非法: ") + evt);
            }
            CodeImportGroup.LinkEvent(obj, code, type, subtype);
            Paths.Log(L("  事件绑定 {0}({1})", typeName, subtype));
        }

        // ---------- 5.31) 原始代码替换（mod 可彻底改写任意函数/事件）----------
        try
        {
            var rawN = RawPatch.Apply(group, data, mods);
            if (rawN > 0) Paths.Log(L("  原始代码替换: {0} 个", rawN));
        }
        catch (Exception ex) { Paths.Log(L("  [警告] 原始代码替换失败: {0}", ex.Message)); }

        // ---------- 5.32) 对象事件 Hook（拦截任意对象的任意事件）----------
        try { ApplyObjectHooks(group, data, mods); }
        catch (Exception ex) { Paths.Log(L("  [警告] 对象事件 Hook 失败: {0}", ex.Message)); }

        // ---------- 5.33) 内置函数 Hook（mod 可拦截任意 GM 内置函数）----------
        try { ApplyBuiltinHooks(group, data, mods); }
        catch (Exception ex) { Paths.Log(L("  [警告] 内置函数 Hook 失败: {0}", ex.Message)); }

        // ---------- 5.34) 输入函数重定向（控制台守卫）----------
        // 游戏有 40+ 个对象直接调用 keyboard_check_pressed 等，kbdBlocked 覆盖不全。
        // 把所有输入读取函数重定向到带控制台守卫的包装函数。
        // ⚠️ 这一步需要反编译大量代码块，是部署变慢的主要原因。
        //    用 --fast-deploy 可跳过（章节内控制台输入屏蔽会失效，但部署快很多）。
        PhaseTimer.Mark("5.x 控制器对象 + 内置补丁 + 音频守卫");
        if (!FastDeploy)

            RedirectInputFunctions(group, data,

                Path.Combine(Path.GetDirectoryName(apiDir) ?? ".", "cache"));

        PhaseTimer.Mark("5.34 输入函数重定向");
        // ---------- 5.35) 控制台输入屏蔽补丁 ----------
        // 游戏用 keyboard_check_direct 读输入，GM 侧清不掉；
        // 唯一可行的办法是 patch 游戏自己的输入管理器：控制台打开时直接跳过。
        ApplyConsoleInputBlock(group, data);

        // ---------- 5.4) 游戏增强补丁（菜单循环 / 存档循环 / 全角字距） ----------
        ApplyGamePatches(group, data, mods, gameRoot, chapter);

        // ---------- 5.5) 音频防泄漏重写 ----------
        QueueAudioGuards(group, data);

        // ---------- 6) 引导注入（prepend 到章节初始化对象） ----------
        var boot = data.Code.ByName(bootCodeName);
        if (boot == null)
            throw new InvalidOperationException(L("找不到引导目标代码条目: {0}", bootCodeName));
        group.QueuePrepend(boot, "scr_ntl_init();\n");
        Paths.Log(L("  引导注入 -> {0}", bootCodeName));

        PhaseTimer.Mark("5.35-6 控制台屏蔽 + 增强补丁 + 引导注入");
        // ---------- 7) 二次编译（含事件绑定与引导） ----------
        var result2 = group.Import(true);
        if (!result2.Successful)
        {
            Paths.Log(L("  [错误] 编译失败（阶段2）:"));
            Paths.Log(result2.PrintAllErrors(true));
            throw new InvalidOperationException(L("Neutraled 引导注入编译失败"));
        }
        LastRefSources = refSources;
        Paths.Log(L("  注入完成"));
        PhaseTimer.Mark("7 二次编译 (Import #2)");
    }

    /// <summary>最近一次注入用到的引用源（供写盘前修复使用）。</summary>
    public static List<UndertaleData> LastRefSources { get; private set; } = new();

    /// <summary>游戏体验增强补丁（Neutraled 特色）。</summary>
    private static void ApplyGamePatches(CodeImportGroup group, UndertaleData data, List<ModEntry> mods,
        string? gameRoot, string? chapter)
    {
        void FR(string codeName, string search, string replace, string label)
        {
            if (data.Code.ByName(codeName) == null) { Paths.Log(L("    [跳过] {0}: {1} 不存在", label, codeName)); return; }
            group.QueueFindReplace(codeName, search, replace);
            Paths.Log(L("    增强: {0}", label));
        }

        // 1) 通用菜单循环导航（obj_custommenu 默认边界行为 0=截断 → 1=循环）
        FR("gml_Object_obj_custommenu_Create_0",
           "menuVEdgeBehavior[i] = 0;", "menuVEdgeBehavior[i] = 1;", "菜单竖直循环");
        FR("gml_Object_obj_custommenu_Create_0",
           "menuHEdgeBehavior[i] = 0;", "menuHEdgeBehavior[i] = 1;", "菜单横向循环");

        // 2) 存档菜单循环（mpos 截断 → 循环）
        FR("gml_Object_obj_savemenu_Draw_0",
           "mpos = clamp(mpos, 0, 3);",
           "if (mpos < 0) { mpos = 3; } else if (mpos > 3) { mpos = 0; }",
           "存档菜单循环");

        // 2.5) 设置菜单 / 绑键界面循环导航（取模法：clamp 分支永不触发）
        // ⚠ 取模的模 = 菜单**项数**。原版是 7；装了会往设置菜单里加一项的 mod（例如 Mod Settings）之后就是 8，
        //   模不对会让"最后一项选不到 / 循环错位"。这里抽成常量，将来项数再变只改这一行。
        const int SettingsEntries = 8;      // 原版 7 + mod 追加的 1 项
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.submenucoord[30] -= 1;",
           $"global.submenucoord[30] = (global.submenucoord[30] + {SettingsEntries - 1}) % {SettingsEntries};",
           $"设置菜单竖直循环(上, {SettingsEntries} 项)");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.submenucoord[30] += 1;",
           $"global.submenucoord[30] = (global.submenucoord[30] + 1) % {SettingsEntries};",
           $"设置菜单竖直循环(下, {SettingsEntries} 项)");
        // ★ 取模只在"下/上"那两处；这块 clamp 还是原版的「> 6 就夹回 6」⇒ 第 8 项会被夹掉、永远选不到。
        //   ⚠ 这条补丁曾经在重写 2.6 段时被一起删掉（部署日志没有「解夹」两行 = 又踩了一次），别再删。
        FR("gml_Object_obj_darkcontroller_Step_0",
           "if (global.submenucoord[30] > 6)",
           "if (global.submenucoord[30] > 7)",
           "设置菜单第 8 项(解夹-判断)");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.submenucoord[30] = 6;",
           "global.submenucoord[30] = 7;",
           "设置菜单第 8 项(解夹-赋值)");

        // 2.6) 设置菜单插入「Mod 设置」行（coord 5）—— 完全照游戏自己的写法
        // ★ 参考实现：mods/deltarune_60_fps（BadArtAdventure 的「Mod Settings」，其 mod.json 描述写着
        //   "...and Fast Text Skip under CONFIG > Mod Settings"）。它的做法是：
        //   ① 新行插在第 6 行（y = yy+325），原版「Return to Title」「Back」整行下移 35（→ 360 / 395）；
        //   ② 光标上限从 6 放到 7，按 Z 时把 global.submenu 切到自己的新 id（50）；
        //   ③ 新页用官方布局自绘（行高 35 / 红心 sprite 3695 / 滚动三角 / 说明行）。
        //   我们照此办理；submenu id 取 51（原版占 1-7 / 10-14 / 20-22 / 30-36，60fps 占 50）。
        // ★★ 变体无关的锚点（2026-09-27 教训）：各章的 obj_darkcontroller_Draw_0 **不是同一份代码**！
        //   chapter4：label = draw_text(_xPos, yy + 325, string_hash_to_newline(stringsetloc("Return to Title", "…")));
        //            红心 = draw_sprite(3695, 0, _heartXPos, …)，框高 = var lang_off = langopt([90, 410, 420], [85, 412, 422]);
        //   chapter1：label = draw_text(_xPos, yy + 325, string_hash_to_newline(scr_84_get_lang_string("obj_darkcontroller_slash_Draw_0_gml_95_0")));
        //            红心 = draw_sprite(922, 0, _heartXPos, …)，框高 = 内联字面量 draw_rectangle(xx + 60, yy + 90, xx + 580, yy + 410, false)
        //   ⇒ 原来那组长 search（带 stringsetloc / back_text / 红心 sprite 3695 / langopt）在 chapter1 上**一条都没命中**，
        //     而「增强: {label}」是无条件打印的 ⇒ 日志上完全看不出来（又一次静默失效，只有 --dump 才抓到）。
        //   现在只用**变体无关**的短前缀：draw_text(_xPos, yy + <行号>, 与红心行的尾部
        //   , 0, _heartXPos, yy + 160 + (global.submenucoord[30] * 35));（两者在 chapter1/chapter4 完全一致）。
        //   ★ 顺序也是契约：先把 Back 从 360 挪到 395（此刻 360 只属于 Back），
        //     再把 Return to Title 从 325 挪到 360（此刻 360 已让出来）——反过来会连锁搬错行。
        //   ★ 框高不用动：deploy 永远从 backup 的原版 data.win 出发，原版框高本来就是 [90,410,420]/[85,412,422]；
        //     多出来的第 8 行靠滚动容纳（见 2.9）。Back 在原版里就落在填充区下沿附近，
        //     我们的 ntl_cfg_row(7) 会在光标不在第 8 行时把它藏起来（窗口内看不到就不画）。
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 360,",
           "draw_text(_xPos, yy + 395,",
           "设置菜单行下移(Back)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 325,",
           "draw_text(_xPos, yy + 360,",
           "设置菜单行下移(Return to Title)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 395,",
           "ntl_cfg_row(7); draw_text(_xPos, (yy + 395) - _ntl_cfg_off,",
           "设置菜单滚动(第 8 行 Back)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 360,",
           "ntl_cfg_row(6); draw_text(_xPos, (yy + 360) - _ntl_cfg_off,",
           "设置菜单滚动(第 7 行 回标题)");
        // ★ 这一行的标签必须画在**游戏的 Draw** 里：Neutraled 的界面全在 Draw GUI 层（窗口坐标），
        //   而设置菜单用房间坐标（xx/yy）⇒ GUI 层对不齐。红心不用我们画（官方公式对 coord 5 正好落在本行 y+10）。
        //   ★ 红心公式同时改成「跟随滚动窗口」，滚动条也在这一行之后画（console/PC 两分支同文本 ⇒ 都追加，
        //     运行时只有一个分支会执行）。
        FR("gml_Object_obj_darkcontroller_Draw_0",
           ", 0, _heartXPos, yy + 160 + (global.submenucoord[30] * 35));",
           ", 0, _heartXPos, yy + 160 + ((global.submenucoord[30] - ntl_cfg_scroll()) * 35)); ntl_cfg_row(5); ntl_settings_row_draw(_xPos, _selectXPos, (yy + 325) - _ntl_cfg_off); draw_set_alpha(1); ntl_cfg_scrollbar_draw(xx, yy);",
           "设置菜单第 6 行绘制(Mod 设置)+红心跟随滚动+滚动条");

        // ★ 二级菜单面板（submenu 51）的绘制：锚点用 Draw 里那个**空块** `if (global.submenu == 34)`（控制设置页的占位，
        //   在 menuno==5 块之后 ⇒ 面板画在所有官方行之上）。它在 chapter1/chapter4/原版备份里都只出现 1 次（已验证）。
        //   ★ 这一条 FR 曾在重写 2.6 时被整段删掉，而日志照样打「增强: …」（标签是入队后无条件打印的）⇒
        //     加了自检 (g) 之后才当场抓到「面板绘制缺失」。改注入补丁后**必须看 (g)**。
        //   ★ 前置的 draw_set_alpha(1)：行可见性用 draw_set_alpha 实现，光标停在未滚动位置时最后画的
        //     第 8 行(Back)会把 alpha 留成 0 ⇒ 后面画的面板/说明行整片透明（真机踩过）。
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "if (global.submenu == 34)",
           "draw_set_alpha(1); if (global.submenu == 51) { draw_set_alpha(1); ntl_modmenu_page_draw(xx, yy); } if (global.submenu == 34)",
           "Mod 设置面板绘制(submenu 51)");

        // 2.7) 设置菜单的按键分派：coord 5 = 打开面板 / 6 = Return to Title / 7 = Back
        // ★ 原版在 button1 分支里按 coord 5 / 6 各自绑定「Return to Title」「Back」（console 与非 console
        //   各一份）。这两处判断改成永不命中的 105/106，再由 ntl_settings_row_press() 统一接管 5/6/7
        //   （调用点插在原版 `if (global.submenucoord[30] == 0)` 之前，仍在 button1_p() 分支内
        //   ⇒ 与游戏同帧、同一套按键缓冲，不会出现"同一次按键被吃两次"）。
        FR("gml_Object_obj_darkcontroller_Step_0",
           "if (global.submenucoord[30] == 5)",
           "if (global.submenucoord[30] == 105)",
           "设置菜单第 6 行按键(改由 Neutraled 分派)");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "if (global.submenucoord[30] == 6)",
           "if (global.submenucoord[30] == 106)",
           "设置菜单第 7 行按键(改由 Neutraled 分派)");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "if (global.submenucoord[30] == 0)",
           "if (ntl_settings_row_press()) { } if (global.submenucoord[30] == 0)",
           "设置菜单第 6/7/8 行按键接管");
        // 面板页（submenu 51）的输入：插在 Step 里 submenu==34 那个每帧块之前（menuno==5 块内）
        FR("gml_Object_obj_darkcontroller_Step_0",
           "if (global.submenu == 34)",
           "if (global.submenu == 51) { ntl_modmenu_page_step(); } if (global.submenu == 34)",
           "Mod 设置面板输入(submenu 51)");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.submenucoord[35] += 1;",
           "global.submenucoord[35] = (global.submenucoord[35] + 1) % 9;",
           "绑键界面循环(下)");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.submenucoord[35] -= 1;",
           "global.submenucoord[35] = (global.submenucoord[35] + 8) % 9;",
           "绑键界面循环(上)");

        // 2.8) 对话文本推进（obj_writer）—— 中日韩字符的**字距**
        // ★ 原版 writer 不按字宽排字：Create 里 `hspace = 8`，Draw 里每个字符统一 `wx += hspace;`，
        //   只有 `global.lang == "en" && jpused == 1` 时才再补 8（暗世界 16）px。fnt_mainbig 的 CJK 字形宽 22 px，
        //   而 8 px 的步进会让相邻汉字互相吃掉 14 px ⇒ 真机上一整行糊成一团（nat-6/nat-8 截图）。
        // ★★ 教训（务必记住）：`增强: {label}` 是「已把 FR 排进队列」**无条件**打印的（Injector.cs:390），
        //   并不代表 search 命中。第一版补丁照抄了 `Neutraled\_test\alldump\` 里的 `wx += ((hspace * 7) div 4);`，
        //   但那份 alldump 是**汉化包为基底**时的旧产物，汉化包自带一套排版代码；原版（0 mod）chapter1 里根本没有这句话
        //   ⇒ FR 静默失效。判断 search 是否有效，必须 `--dump` 对象**当前部署产物**，不能用旧 alldump。
        // 修法（两条单行替换，避免多行 search 的换行符风险）：
        //   A) 非 ASCII 且非日语语境时，把这一格的步进抬到「该字形实际宽度 * textscale + 2」；
        //   B) 把 `_hspace`（jpused 的额外补量）对宽字符清零，避免 A 之后再被加一遍。
        //   ⇒ fnt_mainbig 汉字：22+2 = 24 px；fnt_main 小字：11+2 = 13 px（不再叠 3 px）；日语（global.lang == "ja"）完全不动。
        // ★★ 第二版补丁的 off-by-one 教训：第一版写成 `if (wx - hspace < _ntlw)`，把**绝对坐标**（x0）当成**步进量**去比，
        //   条件永远为假 ⇒ 补丁在产物里「存在但零效果」，nat-10 截图与 nat-8 逐像素完全相同才暴露。
        //   教训：补丁的验证不能只看「代码在产物里」，必须看**真机渲染效果**（或至少一个会改变输出的探针）。
        FR("gml_Object_obj_writer_Draw_0",
           "        wx += hspace;",
           "        wx += hspace; if (global.lang != \"ja\" && ord(mychar) > 255) { var _ntlw = (string_width(mychar) * max(textscale, 1)) + 2; if (hspace < _ntlw) { wx += (_ntlw - hspace); } }",
           "文本字距(CJK 按实际字宽推进)");
        FR("gml_Object_obj_writer_Draw_0",
           "var _hspace = (global.darkzone == 1) ? 16 : 8;",
           "var _hspace = (global.darkzone == 1) ? 16 : 8; if (global.lang != \"ja\" && ord(mychar) > 255) { _hspace = 0; }",
           "文本字距(宽字符不吃 jpused 的额外补量)");
        // 2.9) 设置菜单容纳第 8 行：**不伸缩窗口**，改用滚动（用户 m11431）
        // ★ 用户原话：「mod设置及其二级菜单字体太小了，不要伸缩设置窗口，可以设置滚动条来装下更多内容」
        //   ① 字体：入口行与面板改用游戏自己的 mainbig（EmSize 24，部署期 FontMerge 已补 CJK 字形）——
        //      见 api/ntl_settings_row_draw.gml / api/ntl_modmenu_page_draw.gml（旧版切 ntl_font_cjk，只有一半大）；
        //   ② 窗口：框高保持**原版**（deploy 从 backup 的原版 data.win 出发，从来没有被拉伸过）；
        //      多出来的那一行靠滚动容纳 —— 可见窗口 7 行（行高 35），光标走到第 8 项（Back）时整栏上移 35。
        //   滚动量 = ntl_cfg_scroll() = clamp(global.submenucoord[30] - 6, 0, 1)（api/ntl_cfg_scroll.gml）：
        //     coord ≤ 6 ⇒ 窗口 = 0..6（Back 藏起来：它落在原版填充区下沿之外，正是用户说的「Back 溢出」）；
        //     coord = 7 ⇒ 窗口 = 1..7（Back 正好回到原版 Back 所在的 yy+360，Master Volume 让位）。
        // ★ 这一节是**第二遍** FR：同一个 group 里按入队顺序作用在演进文本上 —— 2.6 已经把
        //   Back/Return 挪到 395/360 并插进「Mod 设置」行，这里再补 0..4 行与 6/7 行的滚动。
        // ★★ 所有 search 都用**变体无关**的短前缀 draw_text(<列>, yy + <行号>,
        //   （chapter1 用 scr_84_get_lang_string(...)、chapter4 用 stringsetloc(...)/back_text，
        //     带函数名的长 search 只能命中一个变体 —— 2.6 原来就是这么静默失效的）。
        // ★ 每个标签行前置 ntl_cfg_row(<slot>)：把窗口外的行 draw_set_alpha(0)（api/ntl_cfg_row.gml）。
        //   slot: 0 主音量 / 1 按键 / 2 简化特效 / 3 全屏·自动奔跑(主机) / 4 自动奔跑·边框(主机) /
        //         5 Mod 设置 / 6 回到标题 / 7 Back（5/6/7 由 2.6 的插入语句与本节各自负责）。
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "var _xPos = (global.lang == \"en\") ? (xx + 170) : (xx + 150);",
           "var _xPos = (global.lang == \"en\") ? (xx + 170) : (xx + 150); var _ntl_cfg_off = ntl_cfg_scroll() * 35;",
           "设置菜单滚动偏移(_ntl_cfg_off)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 150,",
           "ntl_cfg_row(0); draw_text(_xPos, (yy + 150) - _ntl_cfg_off,",
           "设置菜单滚动(第 1 行 主音量)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_selectXPos, yy + 150,",
           "draw_text(_selectXPos, (yy + 150) - _ntl_cfg_off,",
           "设置菜单滚动(第 1 行 数值)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 185,",
           "ntl_cfg_row(1); draw_text(_xPos, (yy + 185) - _ntl_cfg_off,",
           "设置菜单滚动(第 2 行 按键)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 220,",
           "ntl_cfg_row(2); draw_text(_xPos, (yy + 220) - _ntl_cfg_off,",
           "设置菜单滚动(第 3 行 简化特效)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_selectXPos, yy + 220,",
           "draw_text(_selectXPos, (yy + 220) - _ntl_cfg_off,",
           "设置菜单滚动(第 3 行 数值)");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 255,",
           "ntl_cfg_row(3); draw_text(_xPos, (yy + 255) - _ntl_cfg_off,",
           "设置菜单滚动(第 4 行 全屏·自动奔跑(主机))");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_selectXPos, yy + 255,",
           "draw_text(_selectXPos, (yy + 255) - _ntl_cfg_off,",
           "设置菜单滚动(第 4 行 数值(主机))");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(xx + 430, yy + 255,",
           "draw_text(xx + 430, (yy + 255) - _ntl_cfg_off,",
           "设置菜单滚动(第 4 行 数值(全屏))");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_xPos, yy + 290,",
           "ntl_cfg_row(4); draw_text(_xPos, (yy + 290) - _ntl_cfg_off,",
           "设置菜单滚动(第 5 行 自动奔跑·边框(主机))");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(_selectXPos, yy + 290,",
           "draw_text(_selectXPos, (yy + 290) - _ntl_cfg_off,",
           "设置菜单滚动(第 5 行 数值(主机))");
        FR("gml_Object_obj_darkcontroller_Draw_0",
           "draw_text(xx + 430, yy + 290,",
           "draw_text(xx + 430, (yy + 290) - _ntl_cfg_off,",
           "设置菜单滚动(第 5 行 数值)");
        // ★ 滚动条的绘制**不在这里**：它跟在 2.6 的「Mod 设置」行那条 FR 的 replace 里（同一条 replace 一起写）。
        //   实测教训：写在这里、search 指向 2.6 插入出来的文本时**静默不命中**（不是「前缀不存在」——那两条
        //   Back 360→395 的链式替换是命中的；原因未明，疑似 UTMT 的 FindReplace 在同一个代码条目内不保证
        //   按入队顺序看到彼此的插入结果）。结论：跨 FR 的文本依赖一律避免，追加写进同一条 replace。

        // 3) 改键增强：同一操作绑定已被占用的键 → 拒绝（原版会静默交换）；F2 保留
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.input_k[dupe] = global.input_k[global.submenucoord[35]];",
           "/* Neutraled: 同键拒绝（不交换） */",
           "改键去重（拒绝重复绑定）");
        FR("gml_Object_obj_darkcontroller_Step_0",
           "global.input_k[global.submenucoord[35]] = new_key;",
           "if (dupe < 0 && new_key != 113) { global.input_k[global.submenucoord[35]] = new_key; }",
           "改键保护（F2 不可绑定）");

        // 4) 全角字距补偿（仅当加载了含宽字形的字体包，如中文 mod）
        bool fullwidth = HasWideGlyphs(data, mods);
        if (fullwidth)
        {
            // 扩展触发条件：日语 或 中文 mod 启用
            FR("gml_GlobalScript_scr_texttype",
               "if (font_set && global.lang == \"ja\")",
               "if (font_set && (global.lang == \"ja\" || (variable_global_exists(\"ntl_fullwidth\") && global.ntl_fullwidth)))",
               "全角字距条件扩展");

            // 把中文字体 index 纳入字距分支（与日文字体同样处理）
            var fMain = IndexOfFont(data, "fnt_main");
            var fBig = IndexOfFont(data, "fnt_mainbig");
            if (fMain >= 0)
                FR("gml_GlobalScript_scr_texttype",
                   "if (myfont == 16)", $"if (myfont == 16 || myfont == {fMain})", $"主字体字距(main={fMain})");
            if (fBig >= 0)
                FR("gml_GlobalScript_scr_texttype",
                   "else if (myfont == 13)", $"else if (myfont == 13 || myfont == {fBig})", $"大字体字距(mainbig={fBig})");

            Paths.Log(L("    全角字距补偿: 已启用（检测到宽字形字体包）"));
        }
        else
        {
            Paths.Log(L("    全角字距补偿: 未启用（无宽字形字体包）"));
        }

        // 5) 字体补全：本地化只交文本、不交字体 ⇒ 把内置字体包的字形补进游戏自己的字体
        //    （原因/做法见 FontMerge 头注释；纯 ASCII 产物不会改动任何字体）
        if (!string.IsNullOrEmpty(gameRoot) && !string.IsNullOrEmpty(chapter))
            FontMerge.Run(data, gameRoot!, chapter!, mods);
    }

    /// <summary>字体资源在 Fonts 表中的索引（-1 = 不存在）。</summary>
    private static int IndexOfFont(UndertaleData data, string name)
    {
        for (int i = 0; i < data.Fonts.Count; i++)
            if (data.Fonts[i].Name?.Content == name) return i;
        return -1;
    }

    /// <summary>检测是否需要全角字距：mod 带宽字形字体包，或基底字体已是中文字体（字形数很多）。</summary>
    private static bool HasWideGlyphs(UndertaleData data, List<ModEntry> mods)
    {
        // 1) 基底字体已是全字符集（如汉化基底）→ 需要补偿
        foreach (var fname in new[] { "fnt_main", "fnt_mainbig" })
        {
            var f = data.Fonts.FirstOrDefault(x => x.Name?.Content == fname);
            if (f != null && f.Glyphs.Count > 500) return true;
        }

        // 2) mod 自带字体包含宽字形
        foreach (var m in mods)
        {
            var fontDir = Path.Combine(m.Dir, "fonts");
            if (!Directory.Exists(fontDir)) continue;
            foreach (var csv in Directory.GetFiles(fontDir, "*.csv"))
            {
                try
                {
                    foreach (var line in File.ReadLines(csv).Take(4000))
                    {
                        var first = line.Split(';', ',').FirstOrDefault()?.Trim();
                        if (string.IsNullOrEmpty(first)) continue;
                        if (!int.TryParse(first, out var cp)) continue;
                        if (cp >= 0x2E80) return true;   // CJK / 假名 / 谚文等宽字形
                    }
                }
                catch { }
            }
            // 无 CSV 时：只要字体包存在就保守启用（中文 mod 常见）
            if (Directory.GetFiles(fontDir, "*.json").Length > 0 &&
                Directory.GetFiles(fontDir, "*.png").Length > 0)
                return true;
        }
        return false;
    }

    /// <summary>音频通道防泄漏：循环/短音效播放前先停同资源实例；stream 复用避免重复创建。</summary>
    /// <summary>对象事件 Hook：在指定对象的事件代码前后插入分派调用。</summary>
    private static void ApplyObjectHooks(CodeImportGroup group, UndertaleData data, List<ModEntry> mods)
    {
        var decls = ObjectHooks.Collect(mods);
        if (decls.Count == 0) return;
        Paths.Log(L("  对象事件 Hook 声明: {0} 条", decls.Count));

        var gctx = new GlobalDecompileContext(data);
        var jsonItems = new List<string>();
        int applied = 0;

        foreach (var d in decls)
        {
            // 事件代码块名：gml_Object_<obj>_<Event>
            var codeName = "gml_Object_" + d.Object + "_" + d.Event;
            var code = data.Code.ByName(codeName);
            if (code == null)
            {
                Paths.Log(L("    [警告] 找不到对象事件: {0}", codeName));
                continue;
            }

            string src;
            try { src = DecompileWith(gctx, data, codeName); }
            catch { continue; }

            var pre = "var _ntlOev = ntl_oev_run(\"" + d.Object + "\", \"" + d.Event + "\", id, \"pre\");\r\n" +
                      "if (_ntlOev[0] == 1) exit;\r\n";
            var post = "\r\nntl_oev_run(\"" + d.Object + "\", \"" + d.Event + "\", id, \"post\");\r\n";

            var patched = pre + src + post;
            try
            {
                group.QueueReplace(code, patched);
                applied++;
                jsonItems.Add("{\"object\":\"" + d.Object + "\",\"event\":\"" + d.Event +
                              "\",\"mode\":\"" + d.Mode + "\",\"handler\":\"" + d.Handler.Replace("\\", "/") +
                              "\",\"moddir\":\"" + d.ModDir.Replace("\\", "/") + "\"}");
                Paths.Log(L("    对象事件: {0}.{1} ({2})", d.Object, d.Event, d.Mode));
            }
            catch { }
        }

        // 写注册表
        if (jsonItems.Count > 0)
        {
            var regPath = Path.Combine(Paths.NeutraledRoot(Paths.DetectGameRoot()), "object-hooks.json");
            try
            {
                Paths.SafeWrite(regPath, "{\"hooks\":[" + string.Join(",", jsonItems) + "]}");
            }
            catch { }
        }
        Paths.Log(L("    对象事件 Hook 应用: {0} 个", applied));
    }

    /// <summary>内置函数 Hook：把游戏中所有对指定内置函数的调用重定向到包装脚本。</summary>
    private static void ApplyBuiltinHooks(CodeImportGroup group, UndertaleData data, List<ModEntry> mods)
    {
        var decls = BuiltinHooks.Collect(mods);
        if (decls.Count == 0) return;

        var funcNames = decls.Select(d => d.Func).Distinct().ToList();
        Paths.Log(L("  内置函数 Hook 声明: {0} 条（涉及 {1} 个函数）", decls.Count, funcNames.Count));

        // ---- 1) 生成包装脚本并注册 ----
        var wrappers = BuiltinHooks.GenerateWrappers(funcNames);
        foreach (var (scriptName, src) in wrappers)
        {
            var codeName = "gml_Script_" + scriptName;
            var code = data.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(data, codeName);
            group.QueueReplace(code, src);

            var existingScript = data.Scripts.ByName(scriptName);
            if (existingScript == null)
            {
                var s = new UndertaleScript();
                s.Name = data.Strings.MakeString(scriptName);
                s.Code = code;
                data.Scripts.Add(s);
            }
            else existingScript.Code = code;
            Paths.Log(L("    包装: {0}", scriptName));
        }

        // ---- 2) 全代码块重定向：func( → ntl_bh_func( ----
        var skipPrefixes = new[] { "gml_Script_ntl_bh_", "gml_Script_ntl_", "gml_Object_obj_ntl_core_" };
        var gctx = new GlobalDecompileContext(data);
        int blocks = 0, repl = 0;

        foreach (var code in data.Code)
        {
            var nm = code.Name?.Content;
            if (string.IsNullOrEmpty(nm)) continue;
            if (skipPrefixes.Any(p => nm.StartsWith(p, StringComparison.Ordinal))) continue;

            string src;
            try { src = DecompileWith(gctx, data, nm); }
            catch { continue; }
            if (string.IsNullOrEmpty(src)) continue;
            if (!funcNames.Any(f => src.Contains(f + "("))) continue;

            var patched = src;
            int local = 0;
            foreach (var fn in funcNames)
            {
                var from = fn + "(";
                var to = "ntl_bh_" + fn + "(";
                int idx;
                while ((idx = patched.IndexOf(from, StringComparison.Ordinal)) >= 0)
                {
                    // 避免重复替换（已经是 ntl_bh_xxx( 的跳过）
                    if (idx >= 7 && patched.Substring(idx - 7, 7) == "ntl_bh_") break;
                    patched = patched.Substring(0, idx) + to + patched.Substring(idx + from.Length);
                    local++;
                    if (local > 3000) break;
                }
            }
            if (local == 0) continue;
            try { group.QueueReplace(code, patched); blocks++; repl += local; }
            catch { }
        }
        Paths.Log(L("    调用重定向: {0} 处（{1} 个代码块）", repl, blocks));
    }

    /// <summary>把游戏里所有输入读取函数重定向到带控制台守卫的包装函数。
    ///
    /// 背景：游戏有 40+ 个对象直接调用 keyboard_check_pressed / keyboard_check 等，
    /// 而 kbdBlocked 只覆盖走 sunkus_kb_check 的那部分。做全局文本替换最可靠。
    /// 替换规则（注意顺序：先长后短，避免子串误替换）：
    ///   keyboard_check_pressed(  -> ntl_kb_guard_pressed(
    ///   keyboard_check_released( -> ntl_kb_guard_released(
    ///   keyboard_check_direct(   -> ntl_kb_guard_direct(
    ///   keyboard_check(          -> ntl_kb_guard_check(</summary>

    /// <summary>代码布局指纹：所有代码对象的名字 + 指令数。
    /// 用它当缓存键 —— 换基底（不同 data.win）必然得到不同键，不会误用旧缓存。</summary>
    private static string InputScanCacheKey(UndertaleData data)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in data.Code)
            sb.Append(c.Name?.Content ?? "").Append('|').Append(c.Instructions.Count).Append('\n');
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static List<string>? InputScanCacheLoad(string cacheDir, string key)
    {
        try
        {
            var p = Path.Combine(cacheDir, "inputscan-" + key + ".txt");
            if (!File.Exists(p)) return null;
            return File.ReadAllLines(p).Where(l => l.Trim().Length > 0).ToList();
        }
        catch { return null; }
    }

    private static void InputScanCacheSave(string cacheDir, string key, List<string> names)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            File.WriteAllLines(Path.Combine(cacheDir, "inputscan-" + key + ".txt"), names);
        }
        catch { }
    }

    private static void RedirectInputFunctions(CodeImportGroup group, UndertaleData data, string cacheDir)
    {
        // 跳过我们自己的脚本（避免自我替换）
        var skipPrefixes = new[] { "gml_Script_ntl_", "gml_Object_obj_ntl_core_" };
        var rules = new (string from, string to)[]
        {
            ("keyboard_check_pressed(",  "ntl_kb_guard_pressed("),
            ("keyboard_check_released(", "ntl_kb_guard_released("),
            ("keyboard_check_direct(",   "ntl_kb_guard_direct("),
            ("keyboard_check(",          "ntl_kb_guard_check("),
        };

        // ★★ 性能：这一步要反编译**大量**代码块（chapter5 约 2.5 万个对象 ≈ 20 秒），
        //    但结果只取决于**基底 data.win 里游戏自身的代码**，与玩家开关哪些 mod 无关。
        //    所以按基底的 SHA-256 缓存"哪些对象含 keyboard_check"，
        //    第二次起（比如只是换了个 mod）直接命中缓存，跳过 20 秒。
        //    ⚠ 试过用"指令级预筛"（读 Call 的 ValueInt 当函数索引）替代：不可行 ——
        //      这个 data.win 里 ValueInt 根本不是函数表索引（实测 396816 越界、7136 解析成无关脚本），
        //      审计显示会漏掉 83 个真实调用点（覆盖率 120 块 → 37 块）。已放弃该方案。
        var baseKey = InputScanCacheKey(data);
        var cachedNames = NoCache ? null : InputScanCacheLoad(cacheDir, baseKey);
        var candidates = new List<UndertaleCode>();
        if (cachedNames != null)
        {
            foreach (var nm2 in cachedNames)
            {
                var c3 = data.Code.ByName(nm2);
                if (c3 != null) candidates.Add(c3);
            }
            Paths.Log(L("    输入扫描: 命中缓存（{0} 个对象，基底 {1}）", candidates.Count, baseKey));
        }
        else
        {
            // ★★ 结构性快判据（**已对照审计证明等价**，见 InputScanAudit / --audit-inputscan）：
            //   不反编译，直接扫指令里挂着的函数引用（Call.ValueFunction，强类型 UndertaleFunction）。
            //   实测 chapter5：逐对象反编译 15.6s → 扫指令 ~0.3s，候选集合完全一致（120 = 120，0 漏 0 多）。
            //   之前那次失败是因为用了 Call.ValueInt（不是函数索引）；ValueFunction 是 UTMT 解析好的强引用 ✓
            foreach (var code in data.Code)
            {
                var nm0 = code.Name?.Content;
                if (string.IsNullOrEmpty(nm0)) continue;
                if (skipPrefixes.Any(p => nm0.StartsWith(p, StringComparison.Ordinal))) continue;
                bool hit = false;
                foreach (var instr in code.Instructions)
                {
                    try
                    {
                        var uf = instr.ValueFunction;
                        if (uf != null && (uf.Name?.Content ?? "").StartsWith("keyboard_", StringComparison.Ordinal)) { hit = true; break; }
                    }
                    catch { }
                }
                if (hit) candidates.Add(code);
            }
        }

        // ★ 复用同一个全局反编译上下文（重建它是最耗时的部分，约 3 秒/次）
        var sharedGctx = new GlobalDecompileContext(data);

        int codeCount = 0, replCount = 0, skipped = 0;
        var foundNames = new List<string>();
        foreach (var code in candidates)
        {
            var nm = code.Name?.Content;
            if (string.IsNullOrEmpty(nm)) continue;

            string src;
            try { src = DecompileWith(sharedGctx, data, nm); }
            catch { continue; }
            if (string.IsNullOrEmpty(src)) continue;
            if (!src.Contains("keyboard_check")) { skipped++; continue; }
            foundNames.Add(nm);

            var patched = src;
            int localRepl = 0;
            foreach (var (from, to) in rules)
            {
                // 简单全局替换（这些函数名不会出现在字符串里）
                int idx;
                while ((idx = patched.IndexOf(from, StringComparison.Ordinal)) >= 0)
                {
                    patched = patched.Substring(0, idx) + to + patched.Substring(idx + from.Length);
                    localRepl++;
                    if (localRepl > 500) break;   // 安全上限
                }
            }
            if (localRepl == 0) continue;

            // 用 ImportGroup 整体替换（保留原字节码结构）
            try
            {
                group.QueueReplace(code, patched);
                codeCount++;
                replCount += localRepl;
            }
            catch { }
        }
        Paths.Log(L("  输入函数重定向: {0} 处（涉及 {1} 个代码块，字节码预筛跳过 {2} 个）", replCount, codeCount, skipped));
        // 未命中缓存时把扫描结果落盘，下次（同一个基底、只换了 mod）直接复用
        if (cachedNames == null)
        {
            InputScanCacheSave(cacheDir, baseKey, foundNames);
            Paths.Log(L("    输入扫描: 已缓存 {0} 个对象（键 {1}）", foundNames.Count, baseKey));
        }
    }

    /// <summary>控制台输入屏蔽：patch 游戏的输入管理器，控制台打开时提前返回。
    ///
    /// 背景：DELTARUNE 用 keyboard_check_direct 读输入（直接读硬件），
    /// GM 的 keyboard_clear / keyboard_set_map 对它无效。唯一可行的办法是
    /// 让游戏自己的输入函数在看到"控制台已打开"时直接跳过。</summary>
    /// <summary>快速部署模式：跳过输入函数重定向（部署快，但章节内控制台输入屏蔽失效）</summary>
    public static bool FastDeploy = false;

    private static void ApplyConsoleInputBlock(CodeImportGroup group, UndertaleData data)
    {
        // ★ 正确做法：用游戏自身的 kbdBlocked 标志（见 sunkus_kb_check），
        //    它在 BeginStep 里设置，游戏所有输入都通过 sunkus_kb_check 过滤。
        //    这里只对少数直接用 keyboard_check_direct 的对象补保险。
        var targets = new[] {
            "gml_Object_obj_overworld_darkness_bullet_maker_Step_0",
            "gml_Object_obj_bullettester_new_Step_0",
            "gml_Object_obj_bullettester_Step_0"
        };
        int patched = 0;
        foreach (var name in targets)
        {
            var code = data.Code.ByName(name);
            if (code == null) continue;
            string src;
            try { src = Decompile(data, name); }
            catch { continue; }

            // 在函数体开头插入屏蔽逻辑
            // 注意：不能清 F2（113），否则控制台自己也开不了
            var guard = "if (variable_global_exists(\"ntl_console_open\") && global.ntl_console_open) " +
                        "{ keyboard_clear(vk_enter); keyboard_clear(vk_escape); keyboard_clear(vk_space); " +
                        "keyboard_clear(vk_left); keyboard_clear(vk_right); keyboard_clear(vk_up); keyboard_clear(vk_down); " +
                        "keyboard_clear(ord(\"Z\")); keyboard_clear(ord(\"X\")); keyboard_clear(ord(\"C\")); " +
                        "keyboard_string = \"\"; return 0; }\r\n";

            // 插入点：脚本资源找第一个 { 之后；事件代码直接插到开头
            string patchedSrc;
            if (name.StartsWith("gml_Object_"))
            {
                patchedSrc = guard + src;
            }
            else
            {
                var idx = src.IndexOf('{');
                if (idx < 0) continue;
                patchedSrc = src.Insert(idx + 1, "\r\n" + guard);
            }

            group.QueueReplace(code, patchedSrc);
            patched++;
            Paths.Log(L("  控制台输入屏蔽: {0}", name));
        }
        if (patched == 0) Paths.Log(L("  [提示] 未找到 scr_input_manager，控制台输入屏蔽跳过"));
    }

    private static void QueueAudioGuards(CodeImportGroup group, UndertaleData data)
    {
        void FR(string codeName, string search, string replace)
        {
            if (data.Code.ByName(codeName) == null) { Paths.Log(L("    [跳过] {0} 不存在", codeName)); return; }
            group.QueueFindReplace(codeName, search, replace);
            Paths.Log(L("    音频防泄漏: {0}", codeName));
        }

        // 短音效：同一声音永远只保留 1 个实例（snd_play/soundplay/sound_play 三处相同文本）
        FR("gml_GlobalScript_snd_play",
           "var _snd = audio_play_sound(arg0, 50, 0);",
           "audio_stop_sound(arg0);\n    var _snd = audio_play_sound(arg0, 50, 0);");

        // 循环音 / BGM
        FR("gml_GlobalScript_mus_loop",
           "_xsndinstance = audio_play_sound(arg0, 90, 1);",
           "audio_stop_sound(arg0);\n    _xsndinstance = audio_play_sound(arg0, 90, 1);");
        FR("gml_GlobalScript_mus_play",
           "_xsndinstance = audio_play_sound(arg0, 90, 0);",
           "audio_stop_sound(arg0);\n    _xsndinstance = audio_play_sound(arg0, 90, 0);");

        // stream 复用：同名 stream 不再重复创建（防句柄泄漏）
        // ★ 2026-10-02 修正：旧版缓存的是**原始 stream 句柄**，而 snd_free / snd_free_all 会销毁持有它的
        //   obj_astream（obj_astream_Destroy_0 = audio_destroy_stream(mystream)），缓存却不清 ⇒ 同一进程里
        //   再次 snd_init 同名音乐会拿到**死流**（表现为「音乐不播放 / 音乐跟不上」）。改为缓存**持有实例**：
        //   instance_exists 判活后复用，持有者已销毁就重建流。
        FR("gml_GlobalScript_snd_init",
           "_mystream = audio_create_stream(initsongvar);\n    _astream = instance_create(0, 0, 134);\n    _astream.mystream = _mystream;\n    return _mystream;",
           "if (!variable_global_exists(\"ntl_stream_cache\")) global.ntl_stream_cache = ds_map_create();\n    _astream = noone;\n    if (ds_map_exists(global.ntl_stream_cache, initsongvar))\n    {\n        _astream = ds_map_find_value(global.ntl_stream_cache, initsongvar);\n        if (!instance_exists(_astream)) { ds_map_delete(global.ntl_stream_cache, initsongvar); _astream = noone; }\n    }\n    if (_astream == noone)\n    {\n        _mystream = audio_create_stream(initsongvar);\n        _astream = instance_create(0, 0, 134);\n        _astream.mystream = _mystream;\n        ds_map_add(global.ntl_stream_cache, initsongvar, _astream);\n    }\n    else\n    {\n        _mystream = _astream.mystream;\n    }\n    return _mystream;");

        // snd_free_all 销毁全部持有者：缓存一并清空，避免留下死句柄
        FR("gml_GlobalScript_snd_free_all",
           "with (134)\n    {\n        instance_destroy();\n    }",
           "with (134)\n    {\n        instance_destroy();\n    }\n    if (variable_global_exists(\"ntl_stream_cache\")) ds_map_clear(global.ntl_stream_cache);");
    }

    private static string BuildManifest(List<ModEntry> mods, Dictionary<string, string> modMain)
    {
        if (mods.Count == 0) return "// (no mods)";
        var sb = new System.Text.StringBuilder();
        foreach (var m in mods)
        {
            var entry = modMain.TryGetValue(m.Id, out var e) ? e : "";
            sb.Append($"ntl_mod_register(\"{Escape(m.Id)}\", \"{Escape(m.Name)}\", \"{Escape(m.Version)}\", \"{entry}\");\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    public static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}