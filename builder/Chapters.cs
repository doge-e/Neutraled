// 本文件由反编译恢复（原文件被一次误操作截断，见 Chapters.cs.broken）
// 功能等价；原中文注释丢失。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

public static class Chapters
{
	public static readonly string[] OfficialNames = new string[8] { "", "The Beginning", "A Cyber's World", "Late Night", "Prophecy", "Festival Day", "", "" };

	public const int OfficialCount = 5;

	public const int OfficialSlots = 7;

	public static List<OfficialPatch> LastOfficialPatches = new List<OfficialPatch>();

	/// <summary>读取用户手动注册的外部章节（Neutraled/chapters-external.json）。
	/// 用 JsonNode 解析：Dictionary&lt;string, object&gt; 走 STJ 会返回 null（踩过，表现为「注册了但章节选择器里没有」）。</summary>
	public static List<Dictionary<string, string>> LoadExternalList(string gameRoot)
	{
		var list = new List<Dictionary<string, string>>();
		try
		{
			var f = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters-external.json");
			if (!File.Exists(f)) return list;
			var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f));
			if (node is not System.Text.Json.Nodes.JsonArray arr) return list;
			foreach (var it in arr)
			{
				if (it is not System.Text.Json.Nodes.JsonObject o) continue;
				var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var kv in o) d[kv.Key] = kv.Value?.ToString() ?? "";
				if (d.TryGetValue("exe", out var ex) && ex.Length > 0) list.Add(d);
			}
		}
		catch (Exception ex) { Paths.Log(L("  [警告] 读外部章节清单失败: ") + ex.Message); }
		return list;
	}
	public static ChapterDecl? ParseSpec(string spec)
	{
		if (string.IsNullOrWhiteSpace(spec))
		{
			return null;
		}
		string text = spec.Trim();
		bool timeline = false;
		if (text.StartsWith("~"))
		{
			timeline = true;
			text = text.Substring(1).Trim();
		}
		string[] array = text.Split(':', 3);
		if (array.Length < 2)
		{
			return null;
		}
		string text2 = array[0].Trim();
		if (!text2.Equals("chapter", StringComparison.OrdinalIgnoreCase) && !text2.Equals("ch", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (!int.TryParse(array[1].Trim(), out var result) || result < 1)
		{
			return null;
		}
		string text3 = ((array.Length >= 3) ? array[2].Trim() : "");
		int? baseSeq = null;
		int num = text3.LastIndexOf('#');
		if (num > 0 && int.TryParse(text3.Substring(num + 1).Trim(), out var result2) && result2 >= 1)
		{
			baseSeq = result2;
			text3 = text3.Substring(0, num).Trim();
		}
		string baseMod = null;
		string baseAuthor = null;
		int num2 = text3.LastIndexOf('@');
		if (num2 > 0)
		{
			string text4 = text3.Substring(num2 + 1).Trim();
			text3 = text3.Substring(0, num2).Trim();
			string[] array2 = text4.Split(':', 2);
			baseMod = array2[0].Trim();
			if (array2.Length > 1)
			{
				baseAuthor = array2[1].Trim();
			}
		}
		return new ChapterDecl
		{
			Order = result,
			Name = text3,
			Timeline = timeline,
			BaseMod = baseMod,
			BaseAuthor = baseAuthor,
			BaseSeq = baseSeq
		};
	}

	public static List<ChapterDecl> Normalize(ModEntry mod)
	{
		List<ChapterDecl> list = new List<ChapterDecl>();
		if (mod.ChaptersRaw == null)
		{
			return list;
		}
		foreach (JsonNode item in mod.ChaptersRaw)
		{
			if (item == null)
			{
				continue;
			}
			ChapterDecl chapterDecl = null;
			if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value))
			{
				chapterDecl = ParseSpec(value);
				if (chapterDecl == null)
				{
					Paths.Log(L("    [警告] 无法解析章节声明: ") + value + L("（期望 Chapter:N:name 或 ~Chapter:N:name）"));
					continue;
				}
			}
			else if (item is JsonObject jsonObject)
			{
				chapterDecl = new ChapterDecl();
				if (jsonObject.TryGetPropertyValue("order", out JsonNode jsonNode) && jsonNode != null && int.TryParse(jsonNode.ToString(), out var result))
				{
					chapterDecl.Order = result;
				}
				if (jsonObject.TryGetPropertyValue("name", out JsonNode jsonNode2) && jsonNode2 != null)
				{
					chapterDecl.Name = jsonNode2.ToString().Trim('"');
				}
				if (jsonObject.TryGetPropertyValue("timeline", out JsonNode jsonNode3) && jsonNode3 != null)
				{
					chapterDecl.Timeline = jsonNode3.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
				}
				if (jsonObject.TryGetPropertyValue("slug", out JsonNode jsonNode4) && jsonNode4 != null)
				{
					chapterDecl.Slug = jsonNode4.ToString().Trim('"');
				}
				if (jsonObject.TryGetPropertyValue("mods", out JsonNode jsonNode5) && jsonNode5 is JsonArray jsonArray)
				{
					foreach (JsonNode item2 in jsonArray)
					{
						chapterDecl.Mods.Add(item2?.ToString().Trim('"') ?? "");
					}
				}
				if (chapterDecl.Order <= 0)
				{
					continue;
				}
			}
			if (chapterDecl != null)
			{
				list.Add(chapterDecl);
			}
		}
		return list;
	}

	public static string MakeId(ChapterDecl d, ModEntry mod)
	{
		string value = Mods.Sanitize(mod.Id);
		if (d.Timeline)
		{
			string value2 = (string.IsNullOrEmpty(d.Slug) ? Mods.Sanitize(d.Name) : Mods.Sanitize(d.Slug));
			return $"timeline:{d.Order}:{value}:{value2}";
		}
		return $"patch:{d.Order}:{value}";
	}

	public static List<ChapterEntry> BuildRegistry(List<ModEntry> allMods, string currentChapter, string gameRootForScan)
	{
		LastOfficialPatches = new List<OfficialPatch>();
		List<ChapterEntry> list = new List<ChapterEntry>();
		for (int i = 1; i <= 7; i++)
		{
			bool flag = i <= 5;
			list.Add(new ChapterEntry
			{
				Id = "official:" + i,
				Order = i,
				Name = ((i < OfficialNames.Length) ? OfficialNames[i] : ""),
				Kind = "official",
				Dir = "chapter" + i + "_" + Paths.ChapterSuffix(gameRootForScan),
				Enabled = flag,
				Note = (flag ? "" : "not available in this game version")
			});
		}
		List<(ChapterEntry, ChapterDecl)> list2 = new List<(ChapterEntry, ChapterDecl)>();
		List<(ModEntry, ChapterDecl)> list3 = new List<(ModEntry, ChapterDecl)>();
		List<(ModEntry, ChapterDecl)> list4 = new List<(ModEntry, ChapterDecl)>();
		foreach (ModEntry allMod in allMods)
		{
			foreach (ChapterDecl item2 in Normalize(allMod))
			{
				bool flag2 = !string.IsNullOrEmpty(item2.Name);
				if (item2.Timeline && !flag2)
				{
					Paths.Log(L("  [错误] {0}: 「~Chapter:{1}」缺少章节名 —— ", allMod.Name, item2.Order) + L("平行时间线必须写成 ~Chapter:N:名字"));
					continue;
				}
				if (!item2.Timeline && !flag2)
				{
					list3.Add((allMod, item2));
					Paths.Log(L("  [官方修改] {0}: Chapter:{1}（叠加到官方第 {2} 章，不产生章节条目）", allMod.Name, item2.Order, item2.Order));
					continue;
				}
				if (!item2.Timeline && flag2)
				{
					list4.Add((allMod, item2));
					continue;
				}
				string text = MakeId(item2, allMod);
				string dir = "ntl_" + text.Replace(":", "_").Replace("/", "_");
				ModExternal kristalExternal = allMod.KristalExternal;
				if (kristalExternal != null && !string.IsNullOrEmpty(kristalExternal.Exe))
				{
					list.Add(new ChapterEntry
					{
						Id = text,
						Order = item2.Order,
						Name = item2.Name,
						Kind = "external",
						Source = allMod.Name,
						Author = allMod.Author,
						Dir = "",
						Mods = ((item2.Mods.Count > 0) ? item2.Mods : new List<string> { allMod.Id }),
						Exe = kristalExternal.Exe,
						Args = kristalExternal.Args,
						Cwd = (string.IsNullOrEmpty(kristalExternal.Cwd) ? (Path.GetDirectoryName(kristalExternal.Exe) ?? "") : kristalExternal.Cwd),
						Note = L("由 {0} 启动（外部引擎）", Path.GetFileName(kristalExternal.Exe))
					});
					Paths.Log(L("  [外部章节] {0}: {1} → {2}（选中时退出游戏并启动它）", allMod.Name, item2.Name, Path.GetFileName(kristalExternal.Exe)));
				}
				else
				{
					string path = Path.Combine(allMod.Dir, "..", "data");
					string text2 = Path.Combine(path, item2.Name, "data.win");
					if (!File.Exists(text2))
					{
						text2 = Path.Combine(path, "data.win");
					}
					ChapterEntry chapterEntry = new ChapterEntry
					{
						Id = text,
						Order = item2.Order,
						Name = item2.Name,
						Kind = "timeline",
						Source = allMod.Name,
						Author = allMod.Author,
						Dir = dir,
						Mods = ((item2.Mods.Count > 0) ? item2.Mods : new List<string> { allMod.Id }),
						OwnData = (File.Exists(text2) ? text2 : "")
					};
					list.Add(chapterEntry);
					list2.Add((chapterEntry, item2));
				}
			}
		}
		LastOfficialPatches = list3.Select<(ModEntry, ChapterDecl), OfficialPatch>(delegate((ModEntry mod, ChapterDecl decl) x)
		{
			OfficialPatch officialPatch = new OfficialPatch();
			(officialPatch.Mod, officialPatch.Decl) = x;
			return officialPatch;
		}).ToList();
		foreach (var item3 in list4)
		{
			ModEntry item = item3.Item1;
			ChapterDecl pd = item3.Item2;
			List<ChapterEntry> list5 = list.Where((ChapterEntry chapterEntry4) => chapterEntry4.Kind == "timeline" && chapterEntry4.Order == pd.Order && string.Equals(chapterEntry4.Name, pd.Name, StringComparison.OrdinalIgnoreCase)).ToList();
			if (!string.IsNullOrEmpty(pd.BaseMod))
			{
				list5 = list5.Where((ChapterEntry c) => c.Source.Contains(pd.BaseMod, StringComparison.OrdinalIgnoreCase) || c.Mods.Any((string m) => m.Contains(pd.BaseMod, StringComparison.OrdinalIgnoreCase))).ToList();
			}
			if (!string.IsNullOrEmpty(pd.BaseAuthor))
			{
				list5 = list5.Where((ChapterEntry c) => c.Author.Contains(pd.BaseAuthor, StringComparison.OrdinalIgnoreCase)).ToList();
			}
			if (list5.Count == 0)
			{
				Paths.Log(L("  [错误] {0}: 「Chapter:{1}:{2}」找不到对应的平行时间线", item.Name, pd.Order, pd.Name) + L("（必须先有 ~Chapter:") + pd.Order + ":" + pd.Name + L("；若只想改官方章节请写成 Chapter:") + pd.Order + L("）"));
			}
			else if (list5.Count > 1 && !pd.BaseSeq.HasValue)
			{
				Paths.Log(L("  [错误] {0}: 「Chapter:{1}:{2}」有 {3} 个同名平行时间线，请用 @来源mod / @mod:作者 / #序列 消歧：", item.Name, pd.Order, pd.Name, list5.Count));
				foreach (ChapterEntry item4 in list5)
				{
					Paths.Log(L("           - {0}（来源 {1}，作者 {2}）", item4.Id, item4.Source, item4.Author));
				}
			}
			else
			{
				ChapterEntry chapterEntry2 = ((pd.BaseSeq.HasValue && pd.BaseSeq.Value <= list5.Count) ? list5[pd.BaseSeq.Value - 1] : list5[0]);
				if (!chapterEntry2.Mods.Contains(item.Id))
				{
					chapterEntry2.Mods.Add(item.Id);
				}
				Paths.Log(L("  [时间线修改] {0}: Chapter:{1}:{2} → 并入 {3}", item.Name, pd.Order, pd.Name, chapterEntry2.Id));
			}
		}
		List<ChapterEntry> source = list.Where((ChapterEntry x) => x.Kind == "timeline").ToList();
		foreach (ChapterEntry e in list.Where((ChapterEntry x) => x.Kind != "official"))
		{
			List<ChapterEntry> list6 = source.Where((ChapterEntry t) => t.Order == e.Order && !string.Equals(t.Id, e.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(t.Name, e.Name, StringComparison.OrdinalIgnoreCase)).ToList();
			if (list6.Count == 0)
			{
				continue;
			}
			(ChapterEntry, ChapterDecl) tuple = list2.FirstOrDefault<(ChapterEntry, ChapterDecl)>(((ChapterEntry entry, ChapterDecl decl) d) => d.entry == e);
			string baseMod = tuple.Item2?.BaseMod;
			string baseAuthor = tuple.Item2?.BaseAuthor;
			int? value = tuple.Item2?.BaseSeq;
			if (!string.IsNullOrEmpty(baseMod))
			{
				list6 = list6.Where((ChapterEntry c) => c.Source.Contains(baseMod, StringComparison.OrdinalIgnoreCase) || c.Mods.Any((string m) => m.Contains(baseMod, StringComparison.OrdinalIgnoreCase))).ToList();
			}
			if (!string.IsNullOrEmpty(baseAuthor))
			{
				list6 = list6.Where((ChapterEntry c) => c.Author.Contains(baseAuthor, StringComparison.OrdinalIgnoreCase)).ToList();
			}
			if (list6.Count == 0)
			{
				Paths.Log(L("  [警告] 章节 {0} 引用「{1}」未找到匹配来源", e.Id, e.Name) + ((baseMod != null) ? (L("（@") + baseMod + ((baseAuthor != null) ? (":" + baseAuthor) : "") + L("）")) : "") + (value.HasValue ? L("（#{0}）", value) : ""));
				e.Note = "base not found";
				e.Enabled = false;
				continue;
			}
			if (list6.Count > 1)
			{
				if (!value.HasValue || value.Value > list6.Count)
				{
					Paths.Log(L("  [警告] 章节 {0} 的引用「{1}」有 {2} 个同名来源，请用 @mod名 / @mod名:作者 / #序列 消歧：", e.Id, e.Name, list6.Count));
					foreach (ChapterEntry item5 in list6)
					{
						Paths.Log(L("           - {0}  (来源 {1}, 作者 {2})", item5.Id, item5.Source, item5.Author));
					}
					e.Note = "ambiguous base";
					continue;
				}
				list6 = new List<ChapterEntry> { list6[value.Value - 1] };
			}
			ChapterEntry chapterEntry3 = list6[0];
			if (tuple.Item2 != null)
			{
				tuple.Item2.ResolvedBaseId = chapterEntry3.Id;
			}
			e.BaseDir = chapterEntry3.Dir;
			e.Dir = "ntl_" + e.Id.Replace(":", "_").Replace("/", "_");
			e.Note = "based on " + chapterEntry3.Id;
			Paths.Log(L("  章节引用: {0} -> 基于 {1}（产物独立）", e.Id, chapterEntry3.Id));
		}
		foreach (ChapterEntry item6 in list)
		{
			if (item6.Kind == "official")
			{
				item6.Enabled = item6.Order <= 5;
				if (!item6.Enabled)
				{
					item6.Note = "not available in this game version";
				}
				continue;
			}
			string path2 = Path.Combine(gameRootForScan, item6.Dir.Replace('/', Path.DirectorySeparatorChar), "data.win");
			item6.Enabled = File.Exists(path2);
			if (!item6.Enabled)
			{
				item6.Note = "not deployed";
			}
		}
		foreach (var item7 in LoadExternalList(gameRootForScan))
		{
			if (!item7.TryGetValue("exe", out var exePath) || string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) continue;
			var eName = item7.TryGetValue("name", out var nv) && nv.Length > 0 ? nv : Path.GetFileNameWithoutExtension(exePath);
			var eId = item7.TryGetValue("id", out var iv) && iv.Length > 0 ? iv : "external:" + Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
			list.Add(new ChapterEntry
			{
				Id = eId,
				Order = 90,
				Name = eName,
				Kind = "external",
				Source = "external",
				Author = "external",
				Dir = "",
				Mods = new List<string>(),
				Exe = exePath,
				Args = item7.TryGetValue("args", out var av) ? av : "",
				Cwd = item7.TryGetValue("cwd", out var cv) && cv.Length > 0 ? cv : (Path.GetDirectoryName(exePath) ?? ""),
				Note = L("外部程序（--add-external 注册）")
			});
			Paths.Log(L("  [外部章节] {0} → {1}（--add-external 注册）", eName, Path.GetFileName(exePath)));
		}
		return (from chapterEntry4 in list
			orderby chapterEntry4.Order, (!(chapterEntry4.Kind == "official")) ? 1 : 0, chapterEntry4.Name
			select chapterEntry4).ToList();
	}

	public static void WriteRegistry(string gameRoot, List<ChapterEntry> entries)
	{
		string text = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters.json");
		string contents = JsonSerializer.Serialize(new
		{
			version = 1,
			generated = DateTime.Now.ToString("s"),
			maxOrder = ((entries.Count > 0) ? entries.Max((ChapterEntry e) => e.Order) : 7),
			chapters = entries
		}, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});
		Paths.SafeWrite(text, contents);
		Paths.Log(L("  章节注册表: {0} 条 -> {1}", entries.Count, text));
	}
}

// ---- OfficialPatch（反编译恢复）----
public sealed class OfficialPatch
{
	public ModEntry Mod { get; set; } = new ModEntry();

	public ChapterDecl Decl { get; set; } = new ChapterDecl();
}

// ---- ChapterDecl（反编译恢复）----
public sealed class ChapterDecl
{
	public int Order { get; set; }

	public string Name { get; set; } = "";

	public bool Timeline { get; set; }

	public string? Slug { get; set; }

	public List<string> Mods { get; set; } = new List<string>();

	public string? BaseMod { get; set; }

	public string? BaseAuthor { get; set; }

	public int? BaseSeq { get; set; }

	public string? ResolvedBaseId { get; set; }
}

// ---- ChapterEntry（反编译恢复）----
public sealed class ChapterEntry
{
	public string Id { get; set; } = "";

	public int Order { get; set; }

	public string Name { get; set; } = "";

	public string Kind { get; set; } = "official";

	public string Source { get; set; } = "";

	public string Author { get; set; } = "";

	public string Dir { get; set; } = "";

	public List<string> Mods { get; set; } = new List<string>();

	public bool Enabled { get; set; } = true;

	public string Note { get; set; } = "";

	public string BaseDir { get; set; } = "";

	public string OwnData { get; set; } = "";

	public string Exe { get; set; } = "";

	public string Args { get; set; } = "";

	public string Cwd { get; set; } = "";
}
