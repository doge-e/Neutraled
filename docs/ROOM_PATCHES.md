# 房间/地图差分：mod.json 的 `roomPatches` 与 `rooms`（权威口径）

> 面向：写 mod 的人、地图编辑器（`ntl-gui.exe mapedit` / MER）维护者、排查「部署后房间没变」的人。
> 本文件描述的是**当前实现**（Neutraled 1.1.0+，builder/RoomPatches.cs）；字段名即 JSON 键（序列化走 camelCase）。

## 1. 为什么房间要单独一段

`patch`（整脚本覆盖）、`gml/`（函数）、`objects`（对象事件）三段都碰不到 **ROOM 段资源**——
房间宽高、实例、瓦片、图层都在 ROOM 里，不是代码条目。直接改产物 data.win 又破坏「mod 只是增量」的语义
（还会被下一次部署覆盖）。所以地图编辑器在「直接编辑原版 data」模式下保存时，**不写 data.win**，
而是把「相对原版基线的差异」写成 `roomPatches`，新房间写成 `rooms`，由部署器注入每个目标产物。

部署时序：**2.7 新增房间（rooms 段）→ 2.7b 房间差分（roomPatches）→ 2.8 资源名守卫**。
所以同一次部署里，roomPatches 可以改到刚建出来的新房间（按名字找得到）。

## 2. `roomPatches[]`：改已有房间

每条按 `name` 定位原版房间里已有的房间；找不到 → **响亮警告并整条跳过**（不静默吞）。

| 键 | 类型 | 含义 |
|---|---|---|
| `name` | string（必填） | 目标房间名（原版 data 里的名字） |
| `width` / `height` | uint | 房间尺寸 |
| `speed` | uint | 房间速度 |
| `persistent` | bool | 持久房间 |
| `flags` | int | RoomEntryFlags 数值（EnableViews/ShowColor/IsGMS2…） |
| `backgroundColor` | long | 房间背景色（ARGB 数值） |
| `drawBackgroundColor` | bool | 是否绘制背景色 |
| `instancesAdd[]` | 实例 | 新增实例；`instanceId` 缺省或冲突时由部署器分配 |
| `instancesModify[]` | 实例 | 按 `instanceId` 定位，只写非 null 字段 |
| `instancesRemove[]` | `{ "instanceId": N }` | 按 ID 删实例（**对象数组**，不是裸数字数组） |
| `tilesSet[]` | `{ layer?, cells:[{x,y,value}] }` | 原生瓦片逐格写入；`value` 低 28 位 tileId、高 4 位 flags，**0 = 擦除**；`layer` 缺省第一层原生瓦片层 |
| `rename` | string | 房间改名（最后应用）。重名会被拒绝并警告；只改名字，不动房间编号 |
| `removeFromOrder` | bool | 从房间顺序（GeneralInfo.RoomOrder）摘除；**「删除房间」的安全表达**，见 §5 |
| `layersAdd[]` | 图层规格 | 新增图层（可带初始内容）；**同名图层已存在时不重复建层，内容叠加到既有层**（日志「[注意] … 内容叠加到既有层」） |
| `layersRemove[]` | string[] | 按名字删图层（连同层内实例/旧式瓦片）；找不到警告；**最后一个图层不删** |
| `legacyTilesSet[]` | `{ layer?, tiles[] }` | 整层替换旧式（LegacyTiles）瓦片：先清空该层再按列表重建（旧式瓦片没有网格索引，无法逐格差分） |

实例字段（`instancesAdd` / `instancesModify`）：

```json
{ "instanceId": 100005, "object": "obj_mainchara", "x": 200, "y": 160,
  "layer": "Compatibility_Instances_Depth_-20", "scaleX": 1.0, "scaleY": 1.0,
  "rotation": 0.0, "color": 4294967295, "imageIndex": 0, "imageSpeed": 1.0, "nonexistent": false }
```

- `object`：add 时必填（对象名）；modify 时可换对象。
- `layer`：所在实例图层名；缺省第一层。
- `instanceId`：0 或缺省 = 部署器分配（取当前最大 ID + 1，保证不撞）。

图层规格（`layersAdd[]` 与 `rooms[].layers[]` 共用）：

```json
{ "name": "qa_layer_add", "type": "instances", "depth": -25, "visible": true,
  "tileset": null,
  "instances": [ { "object": "obj_switchAsyncHelper", "x": 10, "y": 20 } ],
  "tiles":     [ { "x": 1, "y": 1, "value": 1 } ],
  "legacyTiles": [ { "sprite": "spr_heartsmall", "x": 5, "y": 5, "width": 53, "height": 52,
                     "sourceX": 0, "sourceY": 0, "scaleX": 1.0, "scaleY": 1.0 } ] }
```

- `type`：`instances`（实例层）/ `tiles`（原生瓦片层）/ `assets`（旧式瓦片层）。按 type 取对应字段。
- `depth`：图层深度（越小越靠前）。
- `tiles` 层的 `tileset` 缺省时自动挑一个「有 Texture 且 GMS2TileWidth/Height 有效」的 background；
  图层网格列数/行数按房间尺寸与图块尺寸向上取整。
- 旧式瓦片 `sprite` / `background` 二选一（GMS2 房间通常用 sprite）；`sourceX/sourceY` 是图源偏移；
  `instanceId` 是旧式瓦片自身的 ID（冲突或 0 时由部署器分配）。

## 3. `rooms[]`：新增房间（可带内容）

| 键 | 类型 | 含义 |
|---|---|---|
| `name` | string | 新房间名（与原版/其它 mod 重名会被拒绝） |
| `width` / `height` | uint | 尺寸（默认 640×480） |
| `speed` | uint | 速度（默认 30） |
| `persistent` | bool | 持久 |
| `flags` | int | RoomEntryFlags 数值 |
| `backgroundColor` | long | 背景色 |
| `drawBackgroundColor` | bool | 是否绘制背景色 |
| `views[]` | 视图 | 启用视图（可选） |
| `layers[]` | 图层规格 | 初始图层/实例/瓦片（见 §2）；空 = 纯空房间 |

新房间的编号 = **data.Rooms（ROOM 资源表）的下标**，追加在末尾 ⇒ 原版 ch1 有 147 个房间时，
第一个新房间的编号正好是 147。这是 dojo 之类「room_goto(147)」型 mod 依赖的约定。

## 4. 完整示例（可直接部署的最小 mod）

```json
{
  "id": "demo.roompatch",
  "name": "Room patch demo",
  "author": "doge-e",
  "version": "1.0.0",
  "chapters": ["chapter1"],
  "roomPatches": [
    {
      "name": "room_krisroom",
      "width": 328,
      "instancesModify": [ { "instanceId": 100005, "x": 200, "y": 160 } ],
      "layersAdd": [
        { "name": "demo_layer", "type": "instances", "depth": -25,
          "instances": [ { "object": "obj_switchAsyncHelper", "x": 12, "y": 34 } ] }
      ],
      "tilesSet": [ { "layer": "Compatibility_Tiles_Depth_15000", "cells": [ { "x": 0, "y": 0, "value": 0 } ] } ]
    }
  ],
  "rooms": [
    { "name": "demo_new_room", "width": 320, "height": 240, "speed": 30,
      "layers": [ { "name": "Instances", "type": "instances", "depth": 0,
                    "instances": [ { "object": "obj_switchAsyncHelper", "x": 40, "y": 40 } ] } ] }
  ]
}
```

## 5. 改名 vs「删除房间」：为什么没有物理删除

- **改名**（`rename`）只改名字，不动编号、不动 RoomOrder、不动已编译字节码 ⇒ 结构性安全。
  风险只在「按名字引用」：GML 里的标识符引用会在编译期响亮报错，运行时字符串引用会静默 -1
  （资源名守卫会告警）。引擎特殊房间（`ROOM_` 开头的初始化/日志房间）请勿改名。
  **引用旧名的配置也要跟着改**：地图编辑器（mer）改名时会把战斗/对话/剧情的绑定房间与触发房间
  一起改成新名（状态栏提示「同步 N 处」）；手写 mod.json 若仍绑旧名，运行时会匹配不到，
  编辑器校验也会报「data.win 里没有房间 <旧名>」并拒绝生成 mod。
- **物理删除原版房间不可行**：房间编号是 ROOM 表下标；删中间房间会让其后**所有**房间编号 -1，
  而原版是 GM 2023.6（< 2023.8），字节码里的房间常量是**裸 int**，UTMT/Neutraled 都不会重写
  ⇒ 会静默跳到错误房间，也会击穿 mod 的「新房间编号 = 原版房间数」约定。
- 所以「删除房间」在产品里一律表达为 `removeFromOrder: true`：**保留房间资源与编号**，
  只从 `GeneralInfo.RoomOrder` 摘除。门链 `room_goto_next/previous` 会跳过它；
  静态 `room_goto(编号)` 仍能进入（文档写明的边界，不是 bug）。
- `room_goto_next` 的顺序来自 RoomOrder；用 `--probe-object <data.win> rooms` 可以查部署前后
  的 RoomOrder 条数与顺序（摘除后条数 -1、房间仍能在 data.Rooms 里找到）。

## 6. 顺序与冲突

- 同一 mod 内：`roomPatches` 数组顺序应用；`rename` 在单条补丁内**最后**执行。
- 多个 mod：按扫描顺序（dependencies / load_after / load_before 拓扑排序后的顺序）应用。
  ⚠ A mod 改名、B mod 按旧名 patch ⇒ B 找不到目标会警告并跳过；请让被依赖的改名 mod 先加载
  （`load_before`），或直接用新名写。
- 同名图层叠加：`layersAdd` 遇到已存在图层名时不建层，内容合并进既有层（实例/瓦片/旧式瓦片分别追加）。

## 7. 验证清单（改完 mod 必跑）

```powershell
# 1) 部署（真机目录；先确认缓存/签名不会跳过）
ntl-gui.exe --deploy --chapter chapter1 --no-root --force

# 2) 查房间顺序 / 条数（摘除、新增房间都在这里看得到）
ntl-gui.exe --probe-object "<游戏根>\chapter1_windows\data.win" rooms

# 3) 查单个房间：宽高/图层/实例/瓦片；给 instId 时打印该实例字段
ntl-gui.exe mapedit room-probe "<游戏根>\chapter1_windows\data.win" room_krisroom
ntl-gui.exe mapedit room-probe "<游戏根>\chapter1_windows\data.win" room_krisroom 100005
```

部署后自检会重新解析产物并检查引导注入、控制器事件、API 脚本数、JSON 可解析等；
房间相关的检查目前靠上面两条探针 + 真机进入房间。

## 8. 边界（诚实说明）

- 新房间只表达「房间头 + 视图 + 图层/实例/瓦片」；**房间创建代码（CreationCodeId）不复制模板**，
  新房间默认无创建代码。
- 图层顺序（`RearrangeLayers`）按 depth 重排；同 depth 的既有相对顺序由部署器决定。
- 旧式瓦片整层替换是「全量重建」：没有逐格 diff，大房间的 patch 体积会明显变大。
- 引擎特殊房间（`ROOM_INITIALIZE` 等）改名/摘除会导致游戏流程异常；编辑器侧已有二次确认。
- 同一个 mod 的补丁不能表达「同时删除又新增同名房间」——请改名后另加新房间。
