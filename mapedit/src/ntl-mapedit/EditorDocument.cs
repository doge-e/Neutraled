using System.Drawing;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// One editing session: a loaded data.win, the current room, the selection, an undo
/// stack of room snapshots and a small cache of rendered room bitmaps.
/// </summary>
public sealed class EditorDocument : IDisposable
{
    public GameData Game { get; }
    public string FilePath { get; private set; }
    public UndertaleRoom? Room { get; private set; }
    public List<UndertaleRoom.GameObject> Selection { get; } = new();
    public UndertaleRoom.Tile? SelectedTile { get; private set; }
    /// <summary>Cell in a native GMS2 tile layer (TilesData), selected when the legacy tile list has no hit.</summary>
    public NativeTileRef? SelectedNativeTile { get; private set; }
    /// <summary>Asset-layer sprite instance selection (LayerAssetsData.Sprites / NineSlices).</summary>
    public UndertaleRoom.SpriteInstance? SelectedSprite { get; private set; }
    public UndertaleRoom.Layer? SelectedSpriteLayer { get; private set; }
    public bool Dirty { get; private set; }
    public HashSet<string> EditedRooms { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Bitmap> _renders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _renderOrder = new();
    private readonly Stack<RoomSnapshot> _undo = new();
    private readonly Stack<RoomSnapshot> _redo = new();
    private bool _backedUp;
    private bool _disposed;

    public event Action? StateChanged;      // title / dirty / undo availability
    public event Action? RoomChanged;       // current room object or its content changed
    public event Action? SelectionChanged;
    public event Action? RoomsChanged;      // room set / room names changed (list needs rebuild)

    public EditorDocument(GameData game, string path)
    {
        Game = game;
        FilePath = path;
    }

    // ---------------------------------------------------------------- rooms

    public void OpenRoom(UndertaleRoom? room)
    {
        if (ReferenceEquals(room, Room)) return;
        Room = room;
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        RoomChanged?.Invoke();
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public string RoomName(UndertaleRoom? room = null) => GameData.Name((room ?? Room)?.Name);

    // ------------------------------------------------------------ selection

    public bool IsSelected(UndertaleRoom.GameObject inst) => Selection.Contains(inst);

    public void SelectOnly(UndertaleRoom.GameObject? inst)
    {
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        Selection.Clear();
        if (inst != null) Selection.Add(inst);
        SelectionChanged?.Invoke();
    }

    public void SelectMany(IEnumerable<UndertaleRoom.GameObject> insts, bool additive)
    {
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        if (!additive) Selection.Clear();
        foreach (var i in insts)
            if (!Selection.Contains(i)) Selection.Add(i);
        SelectionChanged?.Invoke();
    }

    public void ToggleSelection(UndertaleRoom.GameObject inst)
    {
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        if (!Selection.Remove(inst)) Selection.Add(inst);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0 && SelectedTile == null && SelectedNativeTile == null &&
            SelectedSprite == null && _tileSelection.Count == 0 && _nativeSelection.Count == 0) return;
        Selection.Clear();
        SelectedTile = null;
        SelectedNativeTile = null;
        ClearAssetSelection();
        _tileSelection.Clear();
        SelectionChanged?.Invoke();
    }

    public void SelectTile(UndertaleRoom.Tile? tile)
    {
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = tile;
        SelectedNativeTile = null;
        ClearAssetSelection();
        SelectionChanged?.Invoke();
    }

    /// <summary>Select a cell of a native GMS2 tile layer (TilesData); clears every other selection.</summary>
    public void SelectNativeTile(NativeTileRef? tile)
    {
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        SelectedNativeTile = tile;
        SelectionChanged?.Invoke();
    }

    /// <summary>Select an asset-layer sprite instance; clears every other selection.</summary>
    public void SelectSprite(UndertaleRoom.Layer layer, UndertaleRoom.SpriteInstance? sprite)
    {
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        SelectedSprite = sprite;
        SelectedSpriteLayer = sprite == null ? null : layer;
        SelectionChanged?.Invoke();
    }

    private void ClearAssetSelection()
    {
        _nativeSelection.Clear();
        SelectedNativeTile = null;
        SelectedSprite = null;
        SelectedSpriteLayer = null;
    }

    public void InvertSelection()
    {
        if (Room == null) return;
        var next = new List<UndertaleRoom.GameObject>();
        foreach (var (_, inst) in RoomEdit.AllInstances(Room))
            if (!Selection.Contains(inst)) next.Add(inst);
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        Selection.AddRange(next);
        SelectionChanged?.Invoke();
    }

    // -------------------------------------------------------- tile selection
    // 瓦片选择与实例选择互斥（进入瓦片选择会清掉实例选择，反之亦然）。瓦片剪贴板独立于
    // 实例剪贴板：存的是「脱离房间的克隆 + 相对最小角的偏移」，所以复制后删掉原图块不会
    // 掏空剪贴板，粘贴也能落到任意图块图层。

    private readonly List<UndertaleRoom.Tile> _tileSelection = new();
    private readonly List<UndertaleRoom.Tile> _tileClipboard = new();

    // Native tile selection mirrors the legacy one, but stores (layer, cell) refs. The clipboard
    // stores raw uint cell values relative to the selection's min corner, so it survives deletion
    // of the source cells and can be pasted onto any native layer (its layer's tileset applies).
    private readonly List<NativeTileRef> _nativeSelection = new();
    private readonly List<(int Dx, int Dy, uint Value)> _nativeClipboard = new();
    private SizeF _nativeClipboardCell = new(16f, 16f);

    public IReadOnlyList<UndertaleRoom.Tile> TileSelection => _tileSelection;
    public int TileSelectionCount => _tileSelection.Count;
    public int TileClipboardCount => _tileClipboard.Count;

    public SizeF TileClipboardCellSize => _tileClipboard.Count > 0 ? RoomEdit.TileWorldSize(_tileClipboard[0]) : new SizeF(8f, 8f);

    public bool IsTileSelected(UndertaleRoom.Tile tile) => _tileSelection.Contains(tile);

    public void SetTileSelection(IEnumerable<UndertaleRoom.Tile> tiles, bool additive = false)
    {
        if (!additive) _tileSelection.Clear();
        foreach (var t in tiles)
            if (!_tileSelection.Contains(t)) _tileSelection.Add(t);
        Selection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        SelectionChanged?.Invoke();
    }

    public void ClearTileSelection()
    {
        if (_tileSelection.Count == 0) return;
        _tileSelection.Clear();
        SelectionChanged?.Invoke();
    }

    public void ToggleTileSelection(UndertaleRoom.Tile tile)
    {
        if (_tileSelection.Remove(tile)) { SelectionChanged?.Invoke(); return; }
        _tileSelection.Add(tile);
        Selection.Clear();
        SelectedTile = null;
        ClearAssetSelection();
        SelectionChanged?.Invoke();
    }

    public int SelectAllTiles(UndertaleRoom.Layer layer)
    {
        var all = RoomEdit.AllTilesInLayer(layer);
        SetTileSelection(all, false);
        return _tileSelection.Count;
    }

    /// <summary>复制选中图块（相对最小角的偏移），返回复制数。</summary>
    public int CopyTileSelection()
    {
        _tileClipboard.Clear();
        _nativeClipboard.Clear();
        if (_tileSelection.Count == 0) return 0;
        int minX = _tileSelection.Min(t => t.X);
        int minY = _tileSelection.Min(t => t.Y);
        foreach (var t in _tileSelection)
        {
            var c = RoomEdit.CloneTile(t);
            c.X = t.X - minX;
            c.Y = t.Y - minY;
            _tileClipboard.Add(c);
        }
        return _tileClipboard.Count;
    }

    /// <summary>删除选中图块，返回删除数。</summary>
    public int DeleteTileSelection()
    {
        var room = Room;
        if (room == null || _tileSelection.Count == 0) return 0;
        PushUndo();
        int n = 0;
        foreach (var t in _tileSelection.ToList())
            if (RoomEdit.RemoveTile(room, t)) n++;
        _tileSelection.Clear();
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
        return n;
    }

    /// <summary>Clear the selected native tile cell (id=0) with undo. Returns 1 when something was removed.</summary>
    public int DeleteSelectedNativeTile()
    {
        var tile = SelectedNativeTile;
        if (tile == null || Room == null || tile.Value == 0) return 0;
        PushUndo();
        NativeTiles.Set(tile.Layer, tile.X, tile.Y, 0);
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectedNativeTile = null;
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
        return 1;
    }

    /// <summary>Remove the selected asset sprite instance (Sprites or NineSlices) with undo.</summary>
    public bool DeleteSelectedSprite()
    {
        var sprite = SelectedSprite;
        var layer = SelectedSpriteLayer;
        if (sprite == null || layer?.AssetsData == null || Room == null) return false;
        PushUndo();
        bool removed = layer.AssetsData.Sprites?.Remove(sprite) == true;
        if (!removed) removed = layer.AssetsData.NineSlices?.Remove(sprite) == true;
        if (!removed) { DiscardLastUndo(); return false; }
        SelectedSprite = null;
        SelectedSpriteLayer = null;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Edit properties of the selected asset sprite as one undoable operation.</summary>
    public void UpdateSelectedSprite(Action<UndertaleRoom.SpriteInstance> mutate)
    {
        var sprite = SelectedSprite;
        if (sprite == null || Room == null) return;
        PushUndo();
        mutate(sprite);
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public int CutTileSelection()
    {
        int n = CopyTileSelection();
        if (n == 0) return 0;
        DeleteTileSelection();
        return n;
    }

    /// <summary>把瓦片剪贴板粘到 anchorX/anchorY（剪贴板里最小角对应的像素坐标），返回新图块（已选中）。</summary>
    public List<UndertaleRoom.Tile> PasteTiles(UndertaleRoom.Layer layer, int anchorX, int anchorY)
    {
        var created = new List<UndertaleRoom.Tile>();
        var room = Room;
        if (room == null || layer.AssetsData?.LegacyTiles == null || _tileClipboard.Count == 0) return created;
        PushUndo();
        uint nextId = RoomEdit.NextTileId(Game.Data);
        foreach (var src in _tileClipboard)
        {
            var t = RoomEdit.CloneTile(src);
            t.X = anchorX + src.X;
            t.Y = anchorY + src.Y;
            t.TileDepth = layer.LayerDepth;
            t.InstanceID = nextId++;
            layer.AssetsData.LegacyTiles.Add(t);
            created.Add(t);
        }
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SetTileSelection(created, false);
        StateChanged?.Invoke();
        return created;
    }

    // ------------------------------------------------- native tile selection
    // 原生 GMS2 瓦片没有独立对象，选择就是 (layer, cell) 列表；剪贴板存 uint 值 + 相对偏移。

    public IReadOnlyList<NativeTileRef> NativeSelection => _nativeSelection;
    public int NativeSelectionCount => _nativeSelection.Count;
    public int NativeClipboardCount => _nativeClipboard.Count;
    public SizeF NativeClipboardCellSize => _nativeClipboardCell;
    public bool IsNativeSelected(NativeTileRef tile) => _nativeSelection.Contains(tile);

    public void SetNativeSelection(IEnumerable<NativeTileRef> tiles, bool additive = false)
    {
        if (!additive) _nativeSelection.Clear();
        foreach (var t in tiles)
            if (!_nativeSelection.Contains(t)) _nativeSelection.Add(t);
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = null;
        SelectedSprite = null;
        SelectedSpriteLayer = null;
        SelectedNativeTile = _nativeSelection.Count > 0 ? _nativeSelection[^1] : null;
        SelectionChanged?.Invoke();
    }

    public int SelectAllNativeTiles(UndertaleRoom.Layer layer)
    {
        var refs = NativeTiles.Cells(layer).Select(c => new NativeTileRef(layer, c.X, c.Y)).ToList();
        SetNativeSelection(refs, false);
        return refs.Count;
    }

    public void ToggleNativeSelection(NativeTileRef tile)
    {
        if (_nativeSelection.Remove(tile))
        {
            SelectedNativeTile = _nativeSelection.Count > 0 ? _nativeSelection[^1] : null;
            SelectionChanged?.Invoke();
            return;
        }
        _nativeSelection.Add(tile);
        Selection.Clear();
        _tileSelection.Clear();
        SelectedTile = null;
        SelectedSprite = null;
        SelectedSpriteLayer = null;
        SelectedNativeTile = tile;
        SelectionChanged?.Invoke();
    }

    public void ClearNativeSelection()
    {
        if (_nativeSelection.Count == 0 && SelectedNativeTile == null) return;
        _nativeSelection.Clear();
        SelectedNativeTile = null;
        SelectionChanged?.Invoke();
    }

    /// <summary>复制原生选区（相对最小格的偏移 + 原始 uint 值），返回复制数。</summary>
    public int CopyNativeSelection()
    {
        _nativeClipboard.Clear();
        _tileClipboard.Clear();
        if (_nativeSelection.Count == 0) return 0;
        int minX = _nativeSelection.Min(t => t.X);
        int minY = _nativeSelection.Min(t => t.Y);
        _nativeClipboardCell = NativeTileEdit.CellSize(_nativeSelection[0].Layer);
        foreach (var t in _nativeSelection)
            _nativeClipboard.Add((t.X - minX, t.Y - minY, t.Value));
        return _nativeClipboard.Count;
    }

    /// <summary>删除全部原生选区（每个图层一次 TileDataUpdated），返回清除的非空格数。</summary>
    public int DeleteNativeTileSelection()
    {
        if (Room == null || _nativeSelection.Count == 0) return 0;
        PushUndo();
        int changed = 0;
        foreach (var group in _nativeSelection.GroupBy(t => t.Layer))
            changed += NativeTileEdit.EraseCells(group.Key, group.Select(t => new Point(t.X, t.Y)));
        if (changed == 0) { DiscardLastUndo(); return 0; }
        _nativeSelection.Clear();
        SelectedNativeTile = null;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
        return changed;
    }

    public int CutNativeSelection()
    {
        int n = CopyNativeSelection();
        if (n == 0) return 0;
        DeleteNativeTileSelection();
        return n;
    }

    /// <summary>把原生瓦片剪贴板粘到 (cellX, cellY)（剪贴板最小格对应的格子坐标），返回写入的非空格数。</summary>
    public int PasteNativeTiles(UndertaleRoom.Layer layer, int cellX, int cellY)
    {
        if (Room == null || !NativeTiles.IsNative(layer) || _nativeClipboard.Count == 0) return 0;
        PushUndo();
        var pasted = new List<NativeTileRef>();
        int changed = 0;
        foreach (var (dx, dy, value) in _nativeClipboard)
        {
            if (value == 0) continue;
            int x = cellX + dx, y = cellY + dy;
            if (!NativeTiles.Set(layer, x, y, value)) continue;
            changed++;
            pasted.Add(new NativeTileRef(layer, x, y));
        }
        if (changed == 0) { DiscardLastUndo(); return 0; }
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SetNativeSelection(pasted, false);
        StateChanged?.Invoke();
        return changed;
    }

    // ------------------------------------------------------ instance align
    // Loenn 的选择工具里有 align/distribute。DR 的实例没有独立的「宽高」字段，
    // 视觉尺寸 = 该实例当前图像帧的尺寸 × 缩放；有旋转时按「绕中心旋转后的外接矩形」估算
    // （原版旋转是绕 sprite origin 转的，origin 不一定是中心，所以这是近似值，粗对齐够用）。
    // 这里统一在编辑器侧算包围盒，对齐/分布都只改 X/Y，走一次撤销快照。

    public enum AlignMode { Left, CenterX, Right, Top, CenterY, Bottom, DistributeX, DistributeY }

    /// <summary>实例在房间里的估算包围盒（位置 + 图像帧尺寸×缩放；旋转按外接矩形；取不到帧时按 16×16）。</summary>
    public RectangleF InstanceBounds(UndertaleRoom.GameObject inst)
    {
        var bmp = Game.Frame(inst.ObjectDefinition?.Sprite, inst.ImageIndex);
        float w = (bmp?.Width ?? 16) * Math.Abs(inst.ScaleX);
        float h = (bmp?.Height ?? 16) * Math.Abs(inst.ScaleY);
        if (w <= 0.01f) w = 16;
        if (h <= 0.01f) h = 16;
        if (Math.Abs(inst.Rotation % 360f) > 0.01f)
        {
            double rad = inst.Rotation * Math.PI / 180.0;
            double c = Math.Abs(Math.Cos(rad));
            double s = Math.Abs(Math.Sin(rad));
            float rw = (float)(w * c + h * s);
            float rh = (float)(w * s + h * c);
            float cx = inst.X + w / 2f;
            float cy = inst.Y + h / 2f;
            return new RectangleF(cx - rw / 2f, cy - rh / 2f, rw, rh);
        }
        return new RectangleF(inst.X, inst.Y, w, h);
    }

    /// <summary>
    /// 对齐/分布选中的实例（至少 2 个）。返回实际移动的实例数；没动就丢掉空的撤销快照。
    /// DistributeX/DistributeY 保持两端不动、把中间那些实例的「中心」等距排开（与 Loenn 一致）。
    /// </summary>
    public int AlignSelection(AlignMode mode)
    {
        var room = Room;
        if (room == null || Selection.Count < 2) return 0;
        var items = Selection.Select(inst => (Inst: inst, Box: InstanceBounds(inst))).ToList();
        float left = items.Min(t => t.Box.Left);
        float right = items.Max(t => t.Box.Right);
        float top = items.Min(t => t.Box.Top);
        float bottom = items.Max(t => t.Box.Bottom);
        float cx = (left + right) / 2f;
        float cy = (top + bottom) / 2f;
        PushUndo();
        int moved = 0;

        void MoveX(UndertaleRoom.GameObject inst, float x)
        {
            int nx = (int)Math.Round(x);
            if (inst.X == nx) return;
            inst.X = nx;
            moved++;
        }
        void MoveY(UndertaleRoom.GameObject inst, float y)
        {
            int ny = (int)Math.Round(y);
            if (inst.Y == ny) return;
            inst.Y = ny;
            moved++;
        }

        switch (mode)
        {
            case AlignMode.Left:
                foreach (var t in items) MoveX(t.Inst, left);
                break;
            case AlignMode.CenterX:
                foreach (var t in items) MoveX(t.Inst, cx - t.Box.Width / 2f);
                break;
            case AlignMode.Right:
                foreach (var t in items) MoveX(t.Inst, right - t.Box.Width);
                break;
            case AlignMode.Top:
                foreach (var t in items) MoveY(t.Inst, top);
                break;
            case AlignMode.CenterY:
                foreach (var t in items) MoveY(t.Inst, cy - t.Box.Height / 2f);
                break;
            case AlignMode.Bottom:
                foreach (var t in items) MoveY(t.Inst, bottom - t.Box.Height);
                break;
            case AlignMode.DistributeX:
            {
                var order = items.OrderBy(t => t.Box.Left + t.Box.Width / 2f).ToList();
                if (order.Count < 3) break;
                float first = order[0].Box.Left + order[0].Box.Width / 2f;
                float last = order[^1].Box.Left + order[^1].Box.Width / 2f;
                float step = (last - first) / (order.Count - 1);
                for (int i = 1; i < order.Count - 1; i++)
                    MoveX(order[i].Inst, first + step * i - order[i].Box.Width / 2f);
                break;
            }
            case AlignMode.DistributeY:
            {
                var order = items.OrderBy(t => t.Box.Top + t.Box.Height / 2f).ToList();
                if (order.Count < 3) break;
                float first = order[0].Box.Top + order[0].Box.Height / 2f;
                float last = order[^1].Box.Top + order[^1].Box.Height / 2f;
                float step = (last - first) / (order.Count - 1);
                for (int i = 1; i < order.Count - 1; i++)
                    MoveY(order[i].Inst, first + step * i - order[i].Box.Height / 2f);
                break;
            }
        }

        if (moved == 0)
        {
            DiscardLastUndo();
            return 0;
        }
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        StateChanged?.Invoke();
        return moved;
    }

    // ------------------------------------------------------------- clipboard
    // 复制的是「脱离房间的克隆」，所以复制后删掉原实例不会掏空剪贴板。每条记录连它的
    // 源图层名字一起存：粘贴时按名字查图层（找不到就退回第一个实例层），而不是持有
    // 一个图层引用——那样图层一改名粘贴就会写进不存在的层。

    private readonly List<(UndertaleRoom.GameObject Instance, string LayerName)> _clipboard = new();
    public int ClipboardCount => _clipboard.Count;

    public int CopySelection()
    {
        if (Room == null) return 0;
        _clipboard.Clear();
        foreach (var inst in Selection)
        {
            var layer = RoomEdit.LayerOf(Room, inst);
            _clipboard.Add((RoomEdit.Clone(inst), layer != null ? GameData.Name(layer.LayerName) : string.Empty));
        }
        return _clipboard.Count;
    }

    public int CutSelection()
    {
        int n = CopySelection();
        if (n == 0) return 0;
        DeleteSelectedInstances();
        return n;
    }

    /// <summary>Paste the clipboard with an offset; returns the new instances (already selected).</summary>
    public List<UndertaleRoom.GameObject> PasteClipboard(int dx, int dy)
    {
        var created = new List<UndertaleRoom.GameObject>();
        var room = Room;
        if (room == null || _clipboard.Count == 0) return created;
        PushUndo();
        foreach (var (src, layerName) in _clipboard)
        {
            var layer = FindInstanceLayer(layerName) ?? RoomEdit.InstancesLayer(room);
            if (layer == null) break;
            created.Add(RoomEdit.AddInstanceCopy(Game.Data, room, layer, src, dx, dy));
        }
        if (created.Count == 0) return created;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectMany(created, false);
        StateChanged?.Invoke();
        return created;
    }

    /// <summary>Duplicate the selection in place with an offset; returns the copies (already selected).</summary>
    public List<UndertaleRoom.GameObject> DuplicateSelection(int dx, int dy)
    {
        var created = new List<UndertaleRoom.GameObject>();
        var room = Room;
        if (room == null || Selection.Count == 0) return created;
        PushUndo();
        foreach (var src in Selection.ToList())
        {
            var layer = RoomEdit.LayerOf(room, src) ?? RoomEdit.InstancesLayer(room);
            if (layer == null) continue;
            created.Add(RoomEdit.AddInstanceCopy(Game.Data, room, layer, src, dx, dy));
        }
        if (created.Count == 0) return created;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectMany(created, false);
        StateChanged?.Invoke();
        return created;
    }

    public int DeleteSelectedInstances()
    {
        var room = Room;
        if (room == null || Selection.Count == 0) return 0;
        PushUndo();
        int n = Selection.Count;
        foreach (var inst in Selection.ToList()) RoomEdit.RemoveInstance(room, inst);
        Selection.Clear();
        SelectedTile = null;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
        return n;
    }

    private UndertaleRoom.Layer? FindInstanceLayer(string name)
    {
        if (Room?.Layers == null || string.IsNullOrEmpty(name)) return null;
        foreach (var l in Room.Layers)
            if (l.InstancesData?.Instances != null &&
                string.Equals(GameData.Name(l.LayerName), name, StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }

    // ----------------------------------------------------------------- undo

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoLabel => _undo.Count > 0 ? _undo.Peek().RoomName : string.Empty;
    public string RedoLabel => _redo.Count > 0 ? _redo.Peek().RoomName : string.Empty;

    /// <summary>Snapshot the current room. Call right before mutating anything.</summary>
    public void PushUndo()
    {
        if (Room == null) return;
        _redo.Clear();
        _undo.Push(Snapshot(Room));
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 丢弃最近一次 PushUndo：连续绘制/矩形/油漆桶在「这一笔其实什么都没改」时调用，
    /// 避免撤销栈里出现按一次 Ctrl+Z 却什么也没发生的空快照。
    /// </summary>
    public void DiscardLastUndo()
    {
        if (_undo.Count == 0) return;
        _undo.Pop();
        StateChanged?.Invoke();
    }

    public bool Undo()
    {
        if (Room == null || _undo.Count == 0) return false;
        var snap = _undo.Pop();
        _redo.Push(Snapshot(Room));
        Restore(snap);
        return true;
    }

    public bool Redo()
    {
        if (Room == null || _redo.Count == 0) return false;
        var snap = _redo.Pop();
        _undo.Push(Snapshot(Room));
        Restore(snap);
        return true;
    }

    private RoomSnapshot Snapshot(UndertaleRoom room)
    {
        var snap = new RoomSnapshot
        {
            RoomName = GameData.Name(room.Name),
            Width = room.Width,
            Height = room.Height,
            Speed = room.Speed,
            Persistent = room.Persistent,
            BackgroundColor = room.BackgroundColor,
            DrawBackgroundColor = room.DrawBackgroundColor,
        };
        if (room.InstanceCreationOrderIDs?.InstanceIDs != null)
            snap.CreationOrder.AddRange(room.InstanceCreationOrderIDs.InstanceIDs);
        if (room.Layers != null)
        {
            foreach (var layer in room.Layers)
            {
                var ls = new LayerSnapshot
                {
                    Layer = layer,
                    Visible = layer.IsVisible,
                    Depth = layer.LayerDepth,
                    XOffset = layer.XOffset,
                    YOffset = layer.YOffset,
                };
                var list = layer.InstancesData?.Instances;
                if (list != null) ls.Instances = list.Select(RoomEdit.Clone).ToList();
                var tiles = layer.AssetsData?.LegacyTiles;
                if (tiles != null) ls.Tiles = tiles.Select(RoomEdit.CloneTile).ToList();
                var ad = layer.AssetsData;
                if (ad?.Sprites != null) ls.Sprites = ad.Sprites.Select(RoomEdit.CloneSpriteInstance).ToList();
                if (ad?.NineSlices != null) ls.NineSlices = ad.NineSlices.Select(RoomEdit.CloneSpriteInstance).ToList();
                var td = layer.TilesData;
                if (td?.TileData != null)
                {
                    ls.NativeTilesX = td.TilesX;
                    ls.NativeTilesY = td.TilesY;
                    ls.NativeTiles = new uint[td.TileData.Length][];
                    for (int y = 0; y < td.TileData.Length; y++)
                        ls.NativeTiles[y] = td.TileData[y] == null ? Array.Empty<uint>() : (uint[])td.TileData[y].Clone();
                }
                var bg = layer.BackgroundData;
                if (bg != null)
                {
                    ls.HasBackground = true;
                    ls.BgVisible = bg.Visible;
                    ls.BgTiledH = bg.TiledHorizontally;
                    ls.BgTiledV = bg.TiledVertically;
                    ls.BgStretch = bg.Stretch;
                    ls.BgColor = bg.Color;
                    ls.BgSprite = bg.Sprite;
                }
                snap.Layers.Add(ls);
            }
        }
        snap.Selection.AddRange(Selection.Select(s => s.InstanceID));
        return snap;
    }

    private void Restore(RoomSnapshot snap)
    {
        var room = Game.FindRoom(snap.RoomName);
        if (room == null) return;
        if (!ReferenceEquals(room, Room))
        {
            Room = room;
            RoomChanged?.Invoke();
        }
        room.Width = snap.Width;
        room.Height = snap.Height;
        room.Speed = snap.Speed;
        room.Persistent = snap.Persistent;
        room.BackgroundColor = snap.BackgroundColor;
        room.DrawBackgroundColor = snap.DrawBackgroundColor;

        foreach (var ls in snap.Layers)
        {
            var layer = ls.Layer;
            if (layer == null) continue;
            layer.IsVisible = ls.Visible;
            layer.LayerDepth = ls.Depth;
            layer.XOffset = ls.XOffset;
            layer.YOffset = ls.YOffset;
            if (ls.HasBackground && layer.BackgroundData != null)
            {
                var bg = layer.BackgroundData;
                bg.Visible = ls.BgVisible;
                bg.TiledHorizontally = ls.BgTiledH;
                bg.TiledVertically = ls.BgTiledV;
                bg.Stretch = ls.BgStretch;
                bg.Color = ls.BgColor;
                bg.Sprite = ls.BgSprite;
                bg.UpdateScale();
            }
            if (ls.Instances != null && layer.InstancesData != null)
            {
                layer.InstancesData.Instances.Clear();
                foreach (var inst in ls.Instances) layer.InstancesData.Instances.Add(RoomEdit.Clone(inst));
            }
            if (ls.Tiles != null && layer.AssetsData?.LegacyTiles != null)
            {
                var list = layer.AssetsData.LegacyTiles;
                list.Clear();
                foreach (var t in ls.Tiles) list.Add(RoomEdit.CloneTile(t));
            }
            if (layer.AssetsData != null)
            {
                if (ls.Sprites != null)
                {
                    layer.AssetsData.Sprites ??= new UndertaleModLib.UndertalePointerList<UndertaleRoom.SpriteInstance>();
                    layer.AssetsData.Sprites.Clear();
                    foreach (var si in ls.Sprites) layer.AssetsData.Sprites.Add(RoomEdit.CloneSpriteInstance(si));
                }
                if (ls.NineSlices != null)
                {
                    layer.AssetsData.NineSlices ??= new UndertaleModLib.UndertalePointerList<UndertaleRoom.SpriteInstance>();
                    layer.AssetsData.NineSlices.Clear();
                    foreach (var si in ls.NineSlices) layer.AssetsData.NineSlices.Add(RoomEdit.CloneSpriteInstance(si));
                }
            }
            if (ls.NativeTiles != null && layer.TilesData != null)
            {
                var copy = new uint[ls.NativeTiles.Length][];
                for (int y = 0; y < copy.Length; y++) copy[y] = (uint[])ls.NativeTiles[y].Clone();
                layer.TilesData.TileData = copy;
                layer.TilesData.TilesX = ls.NativeTilesX;
                layer.TilesData.TilesY = ls.NativeTilesY;
                layer.TilesData.TileDataUpdated();
            }
        }
        if (room.GameObjects != null)
        {
            room.GameObjects.Clear();
            foreach (var (_, inst) in RoomEdit.AllInstances(room)) room.GameObjects.Add(inst);
        }
        if (room.InstanceCreationOrderIDs?.InstanceIDs != null)
        {
            var ids = room.InstanceCreationOrderIDs.InstanceIDs;
            ids.Clear();
            foreach (int id in snap.CreationOrder) ids.Add(id);
        }
        RoomEdit.SortLayers(room);

        SelectedTile = null;
        ClearAssetSelection();
        Selection.Clear();
        _tileSelection.Clear();
        foreach (var (_, inst) in RoomEdit.AllInstances(room))
            if (snap.Selection.Contains(inst.InstanceID)) Selection.Add(inst);

        InvalidateRender(snap.RoomName);
        EditedRooms.Add(snap.RoomName);
        Dirty = true;
        RoomChanged?.Invoke();
        SelectionChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private sealed class RoomSnapshot
    {
        public string RoomName = "";
        public uint Width, Height, Speed;
        public bool Persistent, DrawBackgroundColor;
        public uint BackgroundColor;
        public readonly List<LayerSnapshot> Layers = new();
        public readonly List<int> CreationOrder = new();
        public readonly List<uint> Selection = new();
    }

    /// <summary>Per-layer undo state. Layer edits (depth reorder, offset, visibility, background
    /// properties) are restored by object reference, so reordering room.Layers cannot make undo
    /// write into the wrong layer.</summary>
    private sealed class LayerSnapshot
    {
        public UndertaleRoom.Layer Layer = null!;
        public bool Visible;
        public int Depth;
        public float XOffset, YOffset;
        public List<UndertaleRoom.GameObject>? Instances;
        public List<UndertaleRoom.Tile>? Tiles;
        public List<UndertaleRoom.SpriteInstance>? Sprites;
        public List<UndertaleRoom.SpriteInstance>? NineSlices;
        public uint[][]? NativeTiles;
        public uint NativeTilesX, NativeTilesY;
        public bool HasBackground;
        public bool BgVisible, BgTiledH, BgTiledV, BgStretch;
        public uint BgColor;
        public UndertaleSprite? BgSprite;
    }

    // ------------------------------------------------------ structural edits
    // Adding/removing rooms or layers clears the undo stack: RoomSnapshot restores layer
    // contents by list index, which is meaningless once the layer list itself changes.

    public UndertaleRoom? NewRoom(string name, bool withContent)
    {
        if (Room == null) return null;
        var room = RoomEdit.CreateRoom(Game.Data, Room, name, withContent);
        AfterRoomsChanged();
        OpenRoom(room);
        return room;
    }

    public UndertaleRoom? DuplicateRoom(string? name)
    {
        if (Room == null) return null;
        var room = RoomEdit.DuplicateRoom(Game.Data, Room, name);
        AfterRoomsChanged();
        OpenRoom(room);
        return room;
    }

    public bool RenameRoom(string newName)
    {
        var room = Room;
        if (room == null || string.IsNullOrWhiteSpace(newName)) return false;
        newName = newName.Trim();
        string old = RoomName(room);
        if (string.Equals(old, newName, StringComparison.Ordinal)) return false;
        RoomEdit.RenameRoom(Game.Data, room, newName);
        EditedRooms.Remove(old);
        EditedRooms.Add(newName);
        InvalidateRender(old);
        Dirty = true;
        RoomsChanged?.Invoke();
        RoomChanged?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 改房间尺寸。属性面板的宽度/高度走这里（不要直接改 room.Width/Height），
    /// 这样撤销快照、脏标记与渲染缓存失效都走同一条路——否则改了大小画布还是旧尺寸。
    /// 尺寸会被夹到 16..10000（与属性面板原来的夹取范围一致）。返回是否真的变了。
    /// </summary>
    public bool SetRoomSize(uint width, uint height)
    {
        var room = Room;
        if (room == null) return false;
        width = (uint)Math.Clamp((int)width, 16, 10000);
        height = (uint)Math.Clamp((int)height, 16, 10000);
        if (room.Width == width && room.Height == height) return false;
        PushUndo();
        room.Width = width;
        room.Height = height;
        MarkDirty(RoomName(room));
        InvalidateRender(RoomName(room));
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteRoom()
    {
        var room = Room;
        if (room == null) return false;
        string name = RoomName(room);
        if (!RoomEdit.DeleteRoom(Game.Data, room)) return false;
        EditedRooms.Remove(name);
        InvalidateRender(name);
        Dirty = true;
        Room = null;   // force OpenRoom() to fire even if the next room is the same object
        var next = Game.Rooms.FirstOrDefault();
        if (next == null)
        {
            RoomChanged?.Invoke();
            StateChanged?.Invoke();
        }
        else OpenRoom(next);
        RoomsChanged?.Invoke();
        return true;
    }

    // ------------------------------------------------------ 房间顺序（「删除房间」的可逆表达）

    /// <summary>房间是否已从 GeneralInfo.RoomOrder（玩家流程顺序）里摘除。
    /// ★ 「删除原版房间」不能物理删：房间编号 = ROOM 资源表下标，删中间房间会让其后编号全体 -1，
    ///   而原版（GM 2023.6）字节码里的房间常量是裸 int，不会被重写（会静默跳错房间）。
    ///   所以基线模式下「删除房间」只表达为从 RoomOrder 摘除：房间资源与编号保留，
    ///   门链 room_goto_next/previous 会跳过它，保存时写进 mod 的 removeFromOrder。</summary>
    public bool IsRemovedFromOrder(UndertaleRoom? room)
    {
        if (room == null) return false;
        var order = Game.Data.GeneralInfo?.RoomOrder;
        if (order == null) return false;
        foreach (var e in order)
            if (e != null && ReferenceEquals(e.Resource, room)) return false;
        return true;
    }

    /// <summary>把房间从流程顺序摘除 / 恢复，返回是否真的变了。恢复时按 data.Rooms 里的相邻房间插回原位置附近。
    /// 与删除/改名一致：结构性编辑不进撤销栈。</summary>
    public bool SetRemovedFromOrder(UndertaleRoom room, bool removed)
    {
        var order = Game.Data.GeneralInfo?.RoomOrder;
        if (order == null || room == null) return false;
        int idx = -1;
        for (int i = 0; i < order.Count; i++)
            if (order[i] != null && ReferenceEquals(order[i].Resource, room)) { idx = i; break; }

        if (removed)
        {
            if (idx < 0) return false;                    // 本来就不在顺序里
            order.RemoveAt(idx);
        }
        else
        {
            if (idx >= 0) return false;                   // 本来就在顺序里
            var entry = new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>();
            entry.Resource = room;
            order.Insert(Math.Clamp(RestoreOrderIndex(room), 0, order.Count), entry);
        }

        Dirty = true;
        RoomsChanged?.Invoke();      // 列表要刷新「已摘除」标记
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>恢复顺序时的插入位置：data.Rooms 里往前找第一个仍在 RoomOrder 里的房间，插在它后面；
    /// 找不到就插到最前（原位置信息没有持久化，只能尽量贴近原流程）。</summary>
    private int RestoreOrderIndex(UndertaleRoom room)
    {
        var rooms = Game.Data.Rooms;
        var order = Game.Data.GeneralInfo!.RoomOrder!;
        int roomIdx = -1;
        for (int i = 0; i < rooms.Count; i++)
            if (ReferenceEquals(rooms[i], room)) { roomIdx = i; break; }
        for (int i = roomIdx - 1; i >= 0; i--)
        {
            var prev = rooms[i];
            if (prev == null) continue;
            for (int j = 0; j < order.Count; j++)
                if (order[j] != null && ReferenceEquals(order[j].Resource, prev)) return j + 1;
        }
        return 0;
    }

    // ------------------------------------------------------ structural: layers

    public UndertaleRoom.Layer? AddLayer(bool tiles, int depth)
    {
        var room = Room;
        if (room == null) return null;
        var layer = tiles
            ? RoomEdit.AddTileLayer(Game.Data, room, null, depth)
            : RoomEdit.AddInstanceLayer(Game.Data, room, null, depth);
        AfterLayerChanged();
        return layer;
    }

    public bool RenameLayer(UndertaleRoom.Layer layer, string newName)
    {
        if (Room == null || layer == null || string.IsNullOrWhiteSpace(newName)) return false;
        newName = newName.Trim();
        if (string.Equals(GameData.Name(layer.LayerName), newName, StringComparison.Ordinal)) return false;
        RoomEdit.RenameLayer(Game.Data, layer, newName);
        AfterLayerChanged();
        return true;
    }

    public bool MoveLayer(UndertaleRoom.Layer layer, int delta)
    {
        var room = Room;
        if (room == null || layer == null || !RoomEdit.MoveLayer(room, layer, delta)) return false;
        AfterLayerChanged();
        return true;
    }

    public bool RemoveLayer(UndertaleRoom.Layer layer)
    {
        var room = Room;
        if (room == null || layer == null || !RoomEdit.RemoveLayer(room, layer)) return false;
        AfterLayerChanged();
        return true;
    }

    // ------------------------------------------------------ layer properties
    // Depth edits re-sort room.Layers (that is how GMS2 stores layers in the file); the undo
    // snapshot keeps layer object references, so a reorder cannot shift an undo onto the
    // wrong layer. Offset / visibility / background props are plain per-layer fields.

    public bool SetLayerDepth(UndertaleRoom.Layer layer, int depth)
    {
        var room = Room;
        if (room == null || layer == null || layer.LayerDepth == depth) return false;
        PushUndo();
        layer.LayerDepth = depth;
        RoomEdit.SortLayers(room);
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        RoomChanged?.Invoke();
        return true;
    }

    public bool SetLayerVisible(UndertaleRoom.Layer layer, bool visible)
    {
        var room = Room;
        if (room == null || layer == null || layer.IsVisible == visible) return false;
        PushUndo();
        layer.IsVisible = visible;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        RoomChanged?.Invoke();
        return true;
    }

    public bool SetLayerOffset(UndertaleRoom.Layer layer, float x, float y)
    {
        if (Room == null || layer == null) return false;
        if (Math.Abs(layer.XOffset - x) < 0.0001f && Math.Abs(layer.YOffset - y) < 0.0001f) return false;
        PushUndo();
        layer.XOffset = x;
        layer.YOffset = y;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        RoomChanged?.Invoke();
        return true;
    }

    /// <summary>Edits background-layer properties (only for layers that carry BackgroundData).</summary>
    public bool SetBackgroundProps(UndertaleRoom.Layer layer, bool? visible = null, bool? tiledH = null,
        bool? tiledV = null, bool? stretch = null, uint? color = null)
    {
        if (Room == null || layer == null || layer.BackgroundData == null) return false;
        var bg = layer.BackgroundData;
        bool changed = (visible.HasValue && bg.Visible != visible.Value)
                    || (tiledH.HasValue && bg.TiledHorizontally != tiledH.Value)
                    || (tiledV.HasValue && bg.TiledVertically != tiledV.Value)
                    || (stretch.HasValue && bg.Stretch != stretch.Value)
                    || (color.HasValue && bg.Color != color.Value);
        if (!changed) return false;
        PushUndo();
        if (visible.HasValue) bg.Visible = visible.Value;
        if (tiledH.HasValue) bg.TiledHorizontally = tiledH.Value;
        if (tiledV.HasValue) bg.TiledVertically = tiledV.Value;
        if (stretch.HasValue) bg.Stretch = stretch.Value;
        if (color.HasValue) bg.Color = color.Value;
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        RoomChanged?.Invoke();
        return true;
    }

    /// <summary>更换背景层的精灵（null = 清空）。</summary>
    public bool SetBackgroundSprite(UndertaleRoom.Layer layer, UndertaleSprite? sprite)
    {
        if (Room == null || layer == null || layer.BackgroundData == null) return false;
        var bg = layer.BackgroundData;
        if (ReferenceEquals(bg.Sprite, sprite)) return false;
        PushUndo();
        bg.Sprite = sprite;
        bg.UpdateScale();
        MarkDirty(RoomName());
        InvalidateRender(RoomName());
        RoomChanged?.Invoke();
        return true;
    }

    private void AfterLayerChanged()
    {
        if (Room == null) return;
        string name = RoomName();
        _undo.Clear();
        _redo.Clear();
        InvalidateRender(name);
        MarkDirty(name);
        RoomChanged?.Invoke();
    }

    private void AfterRoomsChanged()
    {
        _undo.Clear();
        _redo.Clear();
        if (Room != null) MarkDirty(RoomName());
        else MarkDirty();
        RoomsChanged?.Invoke();
    }

    // --------------------------------------------------------------- renders

    /// <summary>Rendered room bitmap (cached). Never dispose the returned bitmap.</summary>
    public Bitmap? RenderRoom(UndertaleRoom room)
    {
        string name = GameData.Name(room.Name);
        if (_renders.TryGetValue(name, out var cached)) return cached;
        Bitmap? bmp;
        try
        {
            bmp = RoomRender.Render(Game, room);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[render] {name}: {ex.Message}");
            return null;
        }
        _renders[name] = bmp;
        _renderOrder.Add(name);
        while (_renderOrder.Count > 8)
        {
            var old = _renderOrder[0];
            _renderOrder.RemoveAt(0);
            if (_renders.Remove(old, out var ob)) ob.Dispose();
        }
        return bmp;
    }

    public void InvalidateRender(string? roomName = null)
    {
        if (string.IsNullOrEmpty(roomName))
        {
            foreach (var b in _renders.Values) b.Dispose();
            _renders.Clear();
            _renderOrder.Clear();
            return;
        }
        if (_renders.Remove(roomName, out var bmp)) bmp.Dispose();
        _renderOrder.Remove(roomName);
    }

    public void MarkDirty(string? roomName = null)
    {
        Dirty = true;
        if (!string.IsNullOrEmpty(roomName)) EditedRooms.Add(roomName);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 标记「改动已落盘」。写 data.win 的路径由 Save() 自己清 Dirty；编辑原版基线时 data.win 只读、
    /// 改动写进 sidecar 与生成的 mod，所以需要显式清掉，否则标题与状态栏会一直挂着「* 未保存」。
    /// </summary>
    public void MarkSaved()
    {
        if (!Dirty) return;
        Dirty = false;
        StateChanged?.Invoke();
    }

    // ----------------------------------------------------------------- save

    /// <summary>
    /// Writes the whole data.win back. Keeps a .bak of the previous file.
    /// 原版基线（gameRoot\backup\... / Neutraled\backup\...）默认只读：
    /// 在创建 .bak / .ntlsave.tmp 之前就返回拒绝，原文件与目录里的 sidecar 都不会被触碰。
    /// allowBaselineWrite 仅供显式修复工具使用；GUI 永远传 false。
    /// </summary>
    public SaveResult Save(string? path = null, bool allowBaselineWrite = false, string? gameRoot = null)
    {
        path ??= FilePath;
        if (string.IsNullOrWhiteSpace(path)) return SaveResult.Refused(path ?? "", "没有保存路径");
        var kind = FileRole.Classify(path, gameRoot ?? FileRole.FindGameRoot());
        if (!allowBaselineWrite && kind == FileKind.Baseline)
            return SaveResult.Refused(path, FileRole.BaselineError(path));
        if (!string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) _backedUp = false;
        if (!_backedUp && File.Exists(path))
        {
            try
            {
                var bak = path + ".bak";
                if (!File.Exists(bak)) File.Copy(path, bak);
                _backedUp = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[save] backup failed: {ex.Message}");
            }
        }
        var tmp = path + ".ntlsave.tmp";
        try
        {
            using (var fs = File.Create(tmp))
                UndertaleIO.Write(fs, Game.Data, m => { });
            long len = new FileInfo(tmp).Length;
            if (len < 1024 * 1024)
                throw new InvalidOperationException($"写出的 data.win 只有 {len} 字节，疑似写失败，已放弃覆盖原文件");
            File.Copy(tmp, path, true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }
        FilePath = path;
        Dirty = false;
        StateChanged?.Invoke();
        return SaveResult.Success(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var b in _renders.Values) b.Dispose();
        _renders.Clear();
        Game.Dispose();
    }
}
