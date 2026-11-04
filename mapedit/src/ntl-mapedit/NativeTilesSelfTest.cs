using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// native-tiles-test：在真实 data.win 的工作副本上跑原生 LayerTilesData 的批量编辑闭环。
/// 源文件只读；所有写入都落在 workDir（调用方传 tmp 目录）下。
/// </summary>
public static class NativeTilesSelfTest
{
    private sealed class Candidate
    {
        public UndertaleRoom Room = null!;
        public UndertaleRoom.Layer Layer = null!;
        public UndertaleBackground Background = null!;
        public uint SampleValue;
        public int CellW, CellH, Rows, Ox, Oy;
        public int RectX, RectY, LineX, LineY, FloodX, FloodY;
        public List<Point> Singles = new();
    }

    public static int Run(string src, string workDir)
    {
        int fails = 0;
        void Check(bool ok, string label)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
            if (!ok) fails++;
        }
        void Note(string text) => Console.WriteLine("     " + text);

        try
        {
            if (!File.Exists(src))
            {
                Console.WriteLine("FAIL 源 data.win 不存在: " + src);
                return 1;
            }
            var srcFull = Path.GetFullPath(src);
            var srcInfo = new FileInfo(srcFull);
            if (srcInfo.Length < 1024 * 1024)
            {
                Console.WriteLine("FAIL 源 data.win 太小，疑似不是原版文件: " + srcFull);
                return 1;
            }

            var workFull = Path.GetFullPath(workDir);
            var gameRoot = FileRole.FindGameRoot();
            if (gameRoot != null && IsUnderPath(workFull, gameRoot))
            {
                Console.WriteLine("FAIL workDir 位于真实游戏目录内，拒绝写入: " + workFull);
                return 1;
            }
            if (gameRoot != null &&
                (IsUnderPath(workFull, Path.Combine(gameRoot, "backup")) ||
                 IsUnderPath(workFull, Path.Combine(gameRoot, "Neutraled", "backup"))))
            {
                Console.WriteLine("FAIL workDir 位于备份目录内，拒绝写入: " + workFull);
                return 1;
            }

            Directory.CreateDirectory(workFull);
            var fixture = Path.Combine(workFull, "native-tiles-" + Environment.ProcessId);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
            Directory.CreateDirectory(fixture);
            var sourceWin = Path.Combine(fixture, "data.win");
            File.Copy(srcFull, sourceWin, true);
            // File.Copy 会继承原版 backup 的只读属性；副本必须可写。
            File.SetAttributes(sourceWin, FileAttributes.Normal);

            var outWin = Path.Combine(workFull, "out.win");
            if (File.Exists(outWin)) File.Delete(outWin);
            if (File.Exists(outWin + ".bak")) File.Delete(outWin + ".bak");

            string srcHashBefore = Sha256(srcFull);
            Note("源: " + srcFull);
            Note("fixture: " + fixture);
            Note("out: " + outWin);

            using var gd = new GameData(sourceWin);
            var candidate = FindCandidate(gd);
            if (candidate == null)
            {
                Console.WriteLine("FAIL 没有找到同时满足「有原生非零 tile + 可用 tileset + 足够空白格」的房间/图层");
                return 1;
            }
            var room = candidate.Room;
            var layer = candidate.Layer;
            string roomName = GameData.Name(room.Name);
            string layerName = GameData.Name(layer.LayerName);
            var (cw, ch) = NativeTiles.CellSize(layer);
            Note($"房间: {roomName}  图层: {layerName}  grid={layer.TilesData!.TilesX}x{layer.TilesData.TilesY}  单元格={cw}x{ch}  offset=({layer.XOffset},{layer.YOffset})");
            Note($"tileset: {GameData.Name(candidate.Background.Name)}  rows={candidate.Rows}  baseNativeTiles={NativeTiles.CountNonZero(layer)}");

            using var doc = new EditorDocument(gd, sourceWin);
            doc.OpenRoom(room);
            int baseCount = NativeTiles.CountNonZero(layer);
            string baseHash = Digest(layer);
            Check(baseCount == NativeTiles.CountNonZero(layer) && baseCount > 0, "基线：目标原生图层有非零 tile");

            var brush = NativeTileBrush.FromValue(candidate.Background, candidate.SampleValue);
            Check(brush.TileId > 0, $"笔刷来自原生 tileset（id={brush.TileId} flags={brush.Flags}）");

            // ---- 调色板：枚举原生 tileset + 生成缩略图 ----
            var tilesets = NativeTileEdit.EnumerateTilesets(gd, room);
            var info = tilesets.FirstOrDefault(t => ReferenceEquals(t.Background, candidate.Background));
            Check(tilesets.Count > 0 && info != null,
                $"调色板：枚举到 {tilesets.Count} 个原生 tileset（含目标 tileset）");
            if (info != null)
            {
                Check(info.TileWidth == cw && info.TileHeight == ch,
                    $"调色板：tile 尺寸 {info.TileWidth}x{info.TileHeight} 与图层 {cw}x{ch} 一致");
                using var sheet = NativeTileEdit.BuildSheet(gd, info);
                Check(sheet != null && sheet.Width > 0 && sheet.Height > 0,
                    $"调色板：缩略图生成 {sheet?.Width}x{sheet?.Height}");
            }

            // 形状助手（RoomEdit.TileCellRect/TileCellLine）吃的是「内容坐标」：它们自己按
            // 单元格大小取整，不认识图层 XOffset/YOffset。测试因此不能把图层偏移加进端点，
            // 否则会被助手再除一次，整块形状偏移一格（ch4 offset=(0,-5) 时实测直线 changed=0）。
            // 偏移换算单独由 NativeTileEdit.SnapCell 覆盖（见下方“偏移”断言）。
            Point ContentPx(int x, int y) => new(x * cw, y * ch);
            Point CellOf(Point p) => new(p.X / cw, p.Y / ch);
            var rectCells = RoomEdit.TileCellRect(ContentPx(candidate.RectX, candidate.RectY),
                    ContentPx(candidate.RectX + 3, candidate.RectY + 2), new SizeF(cw, ch))
                .Select(CellOf).Distinct().ToList();
            var lineCells = RoomEdit.TileCellLine(ContentPx(candidate.LineX, candidate.LineY),
                    ContentPx(candidate.LineX + 5, candidate.LineY), new SizeF(cw, ch))
                .Select(CellOf).Distinct().ToList();
            var rectExpected = new List<Point>();
            for (int yy = candidate.RectY; yy <= candidate.RectY + 2; yy++)
                for (int xx = candidate.RectX; xx <= candidate.RectX + 3; xx++)
                    rectExpected.Add(new Point(xx, yy));
            var lineExpected = Enumerable.Range(0, 6)
                .Select(i => new Point(candidate.LineX + i, candidate.LineY)).ToList();
            Check(rectCells.Count == 12, "R：矩形格子数 4x3=12");
            Check(lineCells.Count == 6, "L：直线格子数 6x1=6");
            Check(rectCells.OrderBy(p => p.Y).ThenBy(p => p.X).SequenceEqual(rectExpected),
                "R：矩形格子坐标与内容坐标一致（图层偏移不参与形状取整）");
            Check(lineCells.SequenceEqual(lineExpected),
                "L：直线格子坐标与内容坐标一致（图层偏移不参与形状取整）");
            var snapWorld = new PointF(candidate.Ox + candidate.RectX * cw + cw / 2f,
                candidate.Oy + candidate.RectY * ch + ch / 2f);
            Check(NativeTileEdit.SnapCell(layer, snapWorld, out var snappedCell) &&
                  snappedCell == new Point(candidate.RectX, candidate.RectY),
                $"偏移：SnapCell 把世界坐标还原到图层格 ({candidate.RectX},{candidate.RectY})（offset=({candidate.Ox},{candidate.Oy})）");

            // ---- T: 单格绘制 N 个 ----
            doc.PushUndo();
            int c1 = NativeTileEdit.PaintCells(layer, brush, candidate.Singles);
            int n1 = NativeTiles.CountNonZero(layer);
            Check(c1 == candidate.Singles.Count && n1 == baseCount + c1,
                $"T：单格绘制 {candidate.Singles.Count} 个（changed={c1}, count {baseCount}->{n1}）");

            // ---- R: 矩形 ----
            doc.PushUndo();
            int c2 = NativeTileEdit.PaintCells(layer, brush, rectCells);
            int n2 = NativeTiles.CountNonZero(layer);
            Check(c2 == rectCells.Count && n2 == n1 + c2,
                $"R：矩形绘制 4x3（changed={c2}, count {n1}->{n2}）");

            // ---- L: 直线 ----
            doc.PushUndo();
            int c3 = NativeTileEdit.PaintCells(layer, brush, lineCells);
            int n3 = NativeTiles.CountNonZero(layer);
            Check(c3 == lineCells.Count && n3 == n2 + c3,
                $"L：直线绘制 6x1（changed={c3}, count {n2}->{n3}）");

            string hash3 = Digest(layer);

            // ---- 区域选择 + 复制 / 粘贴 / 剪切 / 删除 ----
            var occupied = NativeTiles.Cells(layer).Select(c => new NativeTileRef(layer, c.X, c.Y)).ToList();
            var pair = new List<NativeTileRef>();
            foreach (var t in occupied)
            {
                var right = occupied.FirstOrDefault(o => o.X == t.X + 1 && o.Y == t.Y);
                if (right != null) { pair.Add(t); pair.Add(right); break; }
            }
            Check(pair.Count == 2, "选区：找到两个相邻非零格");
            if (pair.Count == 2)
            {
                doc.SetNativeSelection(pair);
                Check(doc.NativeSelectionCount == 2, "选区：设置 2 个原生格");
                int copied = doc.CopyNativeSelection();
                Check(copied == 2 && doc.NativeClipboardCount == 2,
                    $"复制：剪贴板 {copied} 格，格子 {doc.NativeClipboardCellSize.Width}x{doc.NativeClipboardCellSize.Height}");

                bool zeroPair = FindZeroHorizontalPair(layer, out int px, out int py);
                Check(zeroPair, "粘贴：找到 2x1 空白目标区");
                if (zeroPair)
                {
                    int beforePaste = NativeTiles.CountNonZero(layer);
                    int pasted = doc.PasteNativeTiles(layer, px, py);
                    int afterPaste = NativeTiles.CountNonZero(layer);
                    Check(pasted == 2 && afterPaste == beforePaste + 2 && doc.NativeSelectionCount == 2,
                        $"粘贴：写入 {pasted} 格到 ({px},{py})（count {beforePaste}->{afterPaste}）");
                    Check(doc.Undo() && NativeTiles.CountNonZero(layer) == beforePaste && Digest(layer) == hash3,
                        "粘贴：undo 恢复批量结果");
                    Check(doc.Redo() && NativeTiles.CountNonZero(layer) == beforePaste + 2,
                        "粘贴：redo 恢复粘贴结果");
                    Check(doc.Undo() && NativeTiles.CountNonZero(layer) == beforePaste && Digest(layer) == hash3,
                        "粘贴：再 undo 回到批量结果");
                }

                doc.SetNativeSelection(pair);
                int cut = doc.CutNativeSelection();
                Check(cut == 2 && NativeTiles.CountNonZero(layer) == n3 - 2 && doc.NativeClipboardCount == 2,
                    $"剪切：{cut} 格（count {n3}->{NativeTiles.CountNonZero(layer)}）");
                Check(doc.Undo() && NativeTiles.CountNonZero(layer) == n3 && Digest(layer) == hash3,
                    "剪切：undo 恢复");

                doc.SetNativeSelection(pair);
                int deleted = doc.DeleteNativeTileSelection();
                Check(deleted == 2 && NativeTiles.CountNonZero(layer) == n3 - 2,
                    $"删除：{deleted} 格（count {n3}->{NativeTiles.CountNonZero(layer)}）");
                Check(doc.Undo() && NativeTiles.CountNonZero(layer) == n3 && Digest(layer) == hash3,
                    "删除：undo 恢复");
            }
            // ---- K: 油漆桶 ----
            doc.PushUndo();
            int c4 = NativeTileEdit.FloodFill(layer, brush, new Point(candidate.FloodX, candidate.FloodY));
            int n4 = NativeTiles.CountNonZero(layer);
            Check(c4 > 0 && n4 == n3 + c4,
                $"K：油漆桶（changed={c4}, count {n3}->{n4}）");
            string finalHash = Digest(layer);
            // 右键擦除走 FloodErase：在同一片连通区域上整片清空，undo 必须恢复油漆桶结果。
            doc.PushUndo();
            int cErase = NativeTileEdit.FloodErase(layer, new Point(candidate.FloodX, candidate.FloodY));
            int nErase = NativeTiles.CountNonZero(layer);
            Check(cErase > 0 && nErase == n4 - cErase,
                $"X：右键 FloodErase 连通区域（changed={cErase}, count {n4}->{nErase}）");
            Check(doc.Undo() && NativeTiles.CountNonZero(layer) == n4 && Digest(layer) == finalHash,
                "X：FloodErase undo 恢复油漆桶结果");

            // ---- undo 全部恢复 ----
            bool undone = true;
            for (int i = 0; i < 4; i++) undone &= doc.Undo();
            int nu = NativeTiles.CountNonZero(layer);
            string hu = Digest(layer);
            Check(undone && nu == baseCount && hu == baseHash,
                $"undo x4：恢复基线（count={nu}, hash {(hu == baseHash ? "一致" : "不一致")}）");

            // ---- redo 全部 ----
            bool redone = true;
            for (int i = 0; i < 4; i++) redone &= doc.Redo();
            int nr = NativeTiles.CountNonZero(layer);
            string hr = Digest(layer);
            Check(redone && nr == n4 && hr == finalHash,
                $"redo x4：恢复批量结果（count={nr}, hash {(hr == finalHash ? "一致" : "不一致")}）");

            Check(doc.Undo(), "undo/redo 穿越：undo #1");
            bool midDifferent = NativeTiles.CountNonZero(layer) != n4 || Digest(layer) != finalHash;
            Check(midDifferent, "undo/redo 穿越：中间态确实不同");
            Check(doc.Redo() && NativeTiles.CountNonZero(layer) == n4 && Digest(layer) == finalHash,
                "undo/redo 穿越：redo #1 回到批量结果");


            // ---- save ----
            var save = doc.Save(outWin, false, fixture);
            Check(save.Ok && File.Exists(outWin), "save：写入 out.win" + (save.Ok ? "" : "（" + save.Error + "）"));
            if (save.Ok)
            {
                Note($"out.win: {new FileInfo(outWin).Length} bytes");
            }
            doc.Dispose();

            // ---- 重新 GameData 读取并比较 ----
            using var gd2 = new GameData(outWin);
            var room2 = gd2.FindRoom(roomName);
            Check(room2 != null, "reload：按房间名重新找到房间");
            if (room2 != null)
            {
                var layer2 = FindLayer(room2, layerName);
                Check(layer2 != null, "reload：按图层名重新找到原生 Tiles 层");
                if (layer2 != null)
                {
                    int n5 = NativeTiles.CountNonZero(layer2);
                    string h5 = Digest(layer2);
                    Check(n5 == n4 && h5 == finalHash,
                        $"reload：计数与内容一致（count={n5}, hash {(h5 == finalHash ? "一致" : "不一致")}）");

                    // ---- reload 后 undo/redo 穿越（擦除单格） ----
                    using var doc2 = new EditorDocument(gd2, outWin);
                    doc2.OpenRoom(room2);
                    var hit = FirstNonZero(layer2);
                    Check(hit.HasValue, "reload：可找到非零格用于穿越测试");
                    if (hit.HasValue)
                    {
                        doc2.PushUndo();
                        int ce = NativeTileEdit.EraseCells(layer2, new[] { hit.Value });
                        int n6 = NativeTiles.CountNonZero(layer2);
                        Check(ce == 1 && n6 == n4 - 1, $"reload 穿越：擦除 1 格（count {n4}->{n6}）");
                        Check(doc2.Undo() && NativeTiles.CountNonZero(layer2) == n4 && Digest(layer2) == finalHash,
                            "reload 穿越：undo 恢复");
                        Check(doc2.Redo() && NativeTiles.CountNonZero(layer2) == n4 - 1,
                            "reload 穿越：redo 擦除");
                        Check(doc2.Undo() && NativeTiles.CountNonZero(layer2) == n4 && Digest(layer2) == finalHash,
                            "reload 穿越：再 undo 回到最终内容");
                    }
                    doc2.Dispose();
                }
            }

            // ---- AddTileLayer：优先创建原生 LayerTilesData ----
            string outLayerWin = Path.Combine(workFull, "out-layer.win");
            if (File.Exists(outLayerWin)) File.Delete(outLayerWin);
            if (File.Exists(outLayerWin + ".bak")) File.Delete(outLayerWin + ".bak");
            using (var layerGd = new GameData(sourceWin))
            {
                var layerRoom = layerGd.FindRoom(roomName);
                Check(layerRoom != null, "AddTileLayer：按房间名找到目标房间");
                if (layerRoom != null)
                {
                    var preferred = layerRoom.Layers?.FirstOrDefault(NativeTiles.IsNative)?.TilesData?.Background;
                    int beforeLayers = layerRoom.Layers?.Count(NativeTiles.IsNative) ?? 0;
                    var added = RoomEdit.AddTileLayer(layerGd.Data, layerRoom, "C3c Native Added", -12345, preferred);
                    int afterLayers = layerRoom.Layers?.Count(NativeTiles.IsNative) ?? 0;
                    Check(added != null && NativeTiles.IsNative(added) && afterLayers == beforeLayers + 1,
                        $"AddTileLayer：新建原生 Tiles 层（原生层 {beforeLayers}->{afterLayers}）");
                    if (added?.TilesData != null)
                    {
                        Check(added.AssetsData?.LegacyTiles == null, "AddTileLayer：未创建 LegacyTiles 兼容层");
                        var td = added.TilesData;
                        Check(td.Background != null && td.TilesX > 0 && td.TilesY > 0,
                            $"AddTileLayer：tileset={GameData.Name(td.Background?.Name)} grid={td.TilesX}x{td.TilesY}");
                        int rows = NativeTiles.Rows(added);
                        bool shape = rows == (int)td.TilesY;
                        for (int y = 0; shape && y < rows; y++) shape = NativeTiles.Cols(added, y) == (int)td.TilesX;
                        Check(shape, "AddTileLayer：每行长度严格等于 TilesX（满足 UTMT 序列化约束）");
                        var addedBrush = NativeTileBrush.FromValue(td.Background, candidate.SampleValue);
                        int painted = NativeTileEdit.PaintCells(added, addedBrush, new[] { new Point(0, 0) });
                        Check(painted == 1 && NativeTiles.CountNonZero(added) == 1,
                            "AddTileLayer：在 (0,0) 写入 1 个原生 tile");
                        string addedName = GameData.Name(added.LayerName);
                        using (var fs = File.Create(outLayerWin)) UndertaleIO.Write(fs, layerGd.Data, m => { });
                        using (var gd3 = new GameData(outLayerWin))
                        {
                            var room3 = gd3.FindRoom(roomName);
                            var added3 = room3?.Layers?.FirstOrDefault(l =>
                                NativeTiles.IsNative(l) &&
                                string.Equals(GameData.Name(l.LayerName), addedName, StringComparison.OrdinalIgnoreCase));
                            Check(added3 != null && NativeTiles.CountNonZero(added3) == 1,
                                $"AddTileLayer：保存后重读，新层 {addedName} 仍有 1 个原生 tile");
                            Note($"out-layer.win: {new FileInfo(outLayerWin).Length} bytes");
                        }
                    }
                }
            }

            string srcHashAfter = Sha256(srcFull);
            Check(srcHashAfter == srcHashBefore, "源文件只读：测试前后 SHA256 不变");
            Check(srcHashBefore != Sha256(outWin), "out.win 与源文件不同（确实写入了内容）");
            Check(File.Exists(outLayerWin) && srcHashBefore != Sha256(outLayerWin),
                "out-layer.win 已生成且与源文件不同");

            Console.WriteLine(fails == 0 ? "RESULT PASS native-tiles-test" : $"RESULT FAIL native-tiles-test ({fails} 项失败)");
            return fails == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL 异常：" + ex);
            return 1;
        }
    }

    private static UndertaleRoom.Layer? FindLayer(UndertaleRoom room, string layerName)
    {
        return room.Layers?.FirstOrDefault(l =>
            NativeTiles.IsNative(l) &&
            string.Equals(GameData.Name(l.LayerName), layerName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderPath(string path, string root)
    {
        string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(r + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Point? FirstNonZero(UndertaleRoom.Layer layer)
    {
        foreach (var (x, y, _) in NativeTiles.Cells(layer)) return new Point(x, y);
        return null;
    }

    private static Candidate? FindCandidate(GameData gd)
    {
        Candidate? fallback = null;
        foreach (var room in gd.Rooms)
        {
            if (room.Layers == null) continue;
            foreach (var layer in room.Layers)
            {
                if (!NativeTiles.IsNative(layer)) continue;
                var bg = layer.TilesData!.Background;
                if (bg?.Texture == null || bg.GMS2TileWidth <= 0 || bg.GMS2TileHeight <= 0) continue;
                if (NativeTiles.CountNonZero(layer) <= 0) continue;
                uint sample = 0;
                bool sampleOk = false;
                foreach (var (_, _, value) in NativeTiles.Cells(layer))
                {
                    int id = NativeTiles.TileId(value);
                    if (!NativeTiles.IsTileIdValid(bg, id)) continue;
                    if (gd.Tile(bg, id, NativeTiles.Flags(value)) == null) continue;
                    sample = value;
                    sampleOk = true;
                    break;
                }
                if (!sampleOk) continue;
                var c = Analyze(room, layer, bg, sample);
                if (c == null) continue;
                bool offset = (int)MathF.Round(layer.XOffset) != 0 || (int)MathF.Round(layer.YOffset) != 0;
                if (offset) return c;
                fallback ??= c;
            }
        }
        return fallback;
    }

    private static Candidate? Analyze(UndertaleRoom room, UndertaleRoom.Layer layer,
        UndertaleBackground bg, uint sample)
    {
        var (w, h) = NativeTiles.CellSize(layer);
        int rows = NativeTiles.Rows(layer);
        if (rows < 3) return null;
        int Ox = (int)MathF.Round(layer.XOffset), Oy = (int)MathF.Round(layer.YOffset);
        var used = new HashSet<(int X, int Y)>();

        if (!FindZeroRect(layer, 4, 3, used, out int rx, out int ry)) return null;
        for (int y = ry; y < ry + 3; y++)
            for (int x = rx; x < rx + 4; x++) used.Add((x, y));

        if (!FindZeroLine(layer, 6, used, out int lx, out int ly)) return null;
        for (int i = 0; i < 6; i++) used.Add((lx + i, ly));

        var singles = new List<Point>();
        for (int y = 0; y < rows && singles.Count < 12; y++)
        {
            int cols = NativeTiles.Cols(layer, y);
            for (int x = 0; x < cols && singles.Count < 12; x++)
            {
                if (used.Contains((x, y))) continue;
                if (!IsZero(layer, x, y)) continue;
                singles.Add(new Point(x, y));
                used.Add((x, y));
            }
        }
        if (singles.Count < 12) return null;

        if (!FindFirstZero(layer, used, out int fx, out int fy)) return null;

        return new Candidate
        {
            Room = room,
            Layer = layer,
            Background = bg,
            SampleValue = sample,
            CellW = w,
            CellH = h,
            Rows = rows,
            Ox = Ox,
            Oy = Oy,
            RectX = rx,
            RectY = ry,
            LineX = lx,
            LineY = ly,
            FloodX = fx,
            FloodY = fy,
            Singles = singles,
        };
    }

    private static bool IsZero(UndertaleRoom.Layer layer, int x, int y) =>
        NativeTiles.TileId(NativeTiles.Get(layer, x, y)) == 0;

    private static bool FindZeroHorizontalPair(UndertaleRoom.Layer layer, out int ox, out int oy)
    {
        ox = oy = 0;
        int rows = NativeTiles.Rows(layer);
        for (int y = 0; y < rows; y++)
        {
            int cols = NativeTiles.Cols(layer, y);
            for (int x = 0; x + 1 < cols; x++)
            {
                if (!IsZero(layer, x, y) || !IsZero(layer, x + 1, y)) continue;
                ox = x; oy = y;
                return true;
            }
        }
        return false;
    }

    private static bool FindZeroRect(UndertaleRoom.Layer layer, int rw, int rh,
        HashSet<(int X, int Y)> used, out int ox, out int oy)
    {
        ox = oy = 0;
        int rows = NativeTiles.Rows(layer);
        for (int y = 0; y + rh <= rows; y++)
        {
            int cols = int.MaxValue;
            for (int yy = y; yy < y + rh; yy++) cols = Math.Min(cols, NativeTiles.Cols(layer, yy));
            for (int x = 0; x + rw <= cols; x++)
            {
                bool ok = true;
                for (int yy = y; yy < y + rh && ok; yy++)
                    for (int xx = x; xx < x + rw; xx++)
                    {
                        if (used.Contains((xx, yy)) || !IsZero(layer, xx, yy)) { ok = false; break; }
                    }
                if (!ok) continue;
                ox = x; oy = y;
                return true;
            }
        }
        return false;
    }

    private static bool FindZeroLine(UndertaleRoom.Layer layer, int len,
        HashSet<(int X, int Y)> used, out int ox, out int oy)
    {
        ox = oy = 0;
        int rows = NativeTiles.Rows(layer);
        for (int y = 0; y < rows; y++)
        {
            int cols = NativeTiles.Cols(layer, y);
            for (int x = 0; x + len <= cols; x++)
            {
                bool ok = true;
                for (int i = 0; i < len; i++)
                    if (used.Contains((x + i, y)) || !IsZero(layer, x + i, y)) { ok = false; break; }
                if (!ok) continue;
                ox = x; oy = y;
                return true;
            }
        }
        return false;
    }

    private static bool FindFirstZero(UndertaleRoom.Layer layer, HashSet<(int X, int Y)> used,
        out int ox, out int oy)
    {
        ox = oy = 0;
        int rows = NativeTiles.Rows(layer);
        for (int y = 0; y < rows; y++)
        {
            int cols = NativeTiles.Cols(layer, y);
            for (int x = 0; x < cols; x++)
            {
                if (used.Contains((x, y)) || !IsZero(layer, x, y)) continue;
                ox = x; oy = y;
                return true;
            }
        }
        return false;
    }

    /// <summary>稳定内容摘要：图层名/尺寸/背景 + 每个 uint（含 0），不与对象身份绑定。</summary>
    private static string Digest(UndertaleRoom.Layer layer)
    {
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void MixString(string s)
        {
            foreach (char ch in s) Mix(ch);
            Mix(0xFFFFFFFFUL);
        }
        MixString(GameData.Name(layer.LayerName));
        Mix((ulong)(int)layer.LayerType);
        MixString(GameData.Name(layer.TilesData?.Background?.Name));
        Mix((ulong)(uint)(int)MathF.Round(layer.XOffset));
        Mix((ulong)(uint)(int)MathF.Round(layer.YOffset));
        Mix(layer.TilesData?.TilesX ?? 0);
        Mix(layer.TilesData?.TilesY ?? 0);
        var rows = layer.TilesData?.TileData;
        Mix((ulong)(rows?.Length ?? 0));
        if (rows != null)
        {
            foreach (var row in rows)
            {
                Mix((ulong)(row?.Length ?? 0));
                if (row == null) continue;
                foreach (uint v in row) Mix(v);
            }
        }
        return h.ToString("X16");
    }

    private static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
