using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

public sealed class SoundDef
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public float Volume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public string Type { get; set; } = "ogg";
    public bool Preload { get; set; } = true;
}

/// <summary>声音资源包导入（sounds/*.json + 音频文件）。
/// 内嵌到 data.win（AudioFile），同名则替换数据。</summary>
public static class SoundImport
{
    public static int Import(UndertaleData data, string soundsDir)
    {
        var jsonFiles = Directory.GetFiles(soundsDir, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0) return 0;

        int replaced = 0, added = 0, skipped = 0;
        foreach (var jf in jsonFiles)
        {
            SoundDef? def;
            try { def = JsonSerializer.Deserialize<SoundDef>(File.ReadAllText(jf), Paths.Json); }
            catch (Exception ex) { Paths.Log(L("    [警告] 解析失败 {0}: {1}", Path.GetFileName(jf), ex.Message)); continue; }
            if (def == null || string.IsNullOrEmpty(def.Name)) continue;

            var file = string.IsNullOrEmpty(def.File) ? def.Name + ".ogg" : def.File;
            var audioPath = Path.Combine(soundsDir, file);
            if (!File.Exists(audioPath))
            {
                var alt = Directory.GetFiles(soundsDir, def.Name + ".*").FirstOrDefault();
                if (alt != null) audioPath = alt; else { Paths.Log(L("    [警告] 音频缺失: {0}", file)); skipped++; continue; }
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(audioPath); }
            catch (Exception ex) { Paths.Log(L("    [警告] 读取失败 {0}: {1}", file, ex.Message)); skipped++; continue; }

            var existing = data.Sounds.FirstOrDefault(s => s.Name?.Content == def.Name);
            if (existing != null)
            {
                // 替换已有声音的数据（保留索引/组，避免引用错位）
                if (existing.AudioFile != null)
                {
                    existing.AudioFile.Data = bytes;
                    existing.Volume = def.Volume;
                    existing.Pitch = def.Pitch;
                    existing.Preload = def.Preload;
                    replaced++;
                }
                else
                {
                    // 外部组声音 → 改为内嵌（保证自包含）
                    var emb = new UndertaleEmbeddedAudio { Name = data.Strings.MakeString(def.Name + "_data"), Data = bytes };
                    data.EmbeddedAudio.Add(emb);
                    existing.AudioFile = emb;
                    existing.AudioID = data.EmbeddedAudio.Count - 1;
                    existing.GroupID = 0;
                    existing.AudioGroup = data.AudioGroups.FirstOrDefault();
                    existing.Volume = def.Volume;
                    existing.Pitch = def.Pitch;
                    replaced++;
                }
            }
            else
            {
                var emb = new UndertaleEmbeddedAudio { Name = data.Strings.MakeString(def.Name + "_data"), Data = bytes };
                data.EmbeddedAudio.Add(emb);
                var snd = new UndertaleSound
                {
                    Name = data.Strings.MakeString(def.Name),
                    Type = data.Strings.MakeString(string.IsNullOrEmpty(def.Type) ? "ogg" : def.Type),
                    File = data.Strings.MakeString(""),
                    Effects = 0,
                    Volume = def.Volume,
                    Pitch = def.Pitch,
                    Preload = def.Preload,
                    AudioFile = emb,
                    AudioID = data.EmbeddedAudio.Count - 1,
                    GroupID = 0,
                    AudioGroup = data.AudioGroups.FirstOrDefault()
                };
                data.Sounds.Add(snd);
                added++;
            }
        }
        Paths.Log(L("    声音: {0} 新增 / {1} 替换 / {2} 跳过", added, replaced, skipped));
        return added + replaced;
    }
}
