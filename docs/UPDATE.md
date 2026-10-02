# 游戏更新（UPDATE）：检测 / 提示 / 采纳新基线

> **本篇包含**（按顺序）：
> - [1. 为什么需要它](#1-为什么需要它) — Steam 更新换回原版后，旧备份会让部署**静默降级**
> - [2. 数据布局](#2-数据布局) — `deploy-state.json` 与 `backup/history/`
> - [3. 判定与退出码](#3-判定与退出码) — 四态结论 + 每种命令的退出码
> - [4. 命令速查](#4-命令速查)
> - [5. 场景演练](#5-场景演练) — 更新后 / 新章节 / 整包基底漂移 / 想回滚
> - [6. 纪律（不可妥协的三条）](#6-纪律不可妥协的三条)
> - [7. 故障排查](#7-故障排查)

---

## 1. 为什么需要它

Neutraled 的部署是「**原版备份 + 增量注入**」：第一次部署时把各章节的 `data.win` 复制进
`Neutraled/backup/`，之后每次部署都拿这份备份当基底。

Steam 更新游戏时会把活跃的 `data.win` **换回原版**（我们注入的内容全部消失）。这时如果直接再部署：

- 基底是**更新前**的旧原版 → 产物实际是旧版本的游戏本体 + mod ⇒ **静默降级**（存档可能不兼容）；
- 新版本新增的章节（`chapter6_windows`…）会被当成普通章节直接注入，玩家根本没同意过。

所以从 1.1 起，所有会**写游戏目录**的命令在动手前都会先做一次更新检测；检测本身**只读**，
检测到变化时**只提示、停下、等用户确认**——绝不自动改本体。

---

## 2. 数据布局

| 路径 | 内容 |
| --- | --- |
| `Neutraled/deploy-state.json` | 基线记录：Steam 版本（appid/buildid/depot manifest）、exe 指纹、各槽位的原版/产物指纹、已确认章节、整包基底指纹、历史归档清单 |
| `Neutraled/backup/data.win`、`Neutraled/backup/chapterN_<suffix>/data.win` | **当前**基线的原版备份（部署时使用的基底） |
| `Neutraled/backup/history/<buildid>/game/…` | 采纳新基线时，**旧备份**按 Steam buildid 归档（对应 `<游戏根>/backup` 那一份） |
| `Neutraled/backup/history/<buildid>/neutraled/…` | 同上（对应 `Neutraled/backup` 那一份） |

> 归档是**移动**，不是删除：任何一次采纳都不会丢掉旧版本的原始 `data.win`。
> 想回滚到旧版本：把 `history/<buildid>/neutraled/` 里的文件拷回 `Neutraled/backup/` 对应路径即可。

`suffix` 是平台后缀（`windows` / `linux` / `unix` / `macos`），与 `Paths.ChapterSuffix` 一致。

---

## 3. 判定与退出码

检测结论（`--update-check` 打印、其它命令内部也用同一套）：

| 结论 | 含义 | 处置 |
| --- | --- | --- |
| `first`（首次运行） | 还没有基线 | 跑一次部署即建立基线 |
| `update` | 活跃 `data.win` 被换回原版 / buildid 或 depot manifest 变了 / exe 变了 | `--adopt-current --yes` 再 `--deploy-all --force` |
| `chapters` | 章节集合变了（新增或消失），本体没变 | 新增的章节需要 `--adopt-current --yes` 确认后才注入 |
| `ok` | 没检测到更新 | 无需处理 |

按槽位还会单独标记：`原版（与备份一致）` / `★ 原版已变（疑似游戏更新）` / `原版（无备份）` /
`产物（已部署）` / `产物缺失`。

命令退出码：

| 命令 | 0 | 2 | 3 |
| --- | --- | --- | --- |
| `--update-check` | 没检测到更新 | 需要处理（更新 / 新章节 / 基底漂移 / 产物缺失） | 检测本身失败 |
| `--deploy` / `--deploy-all` / `--launch` | 成功 | 前置检测拦下（要 `--yes` 或 `--base-mod none` / `--accept-base-drift`） | — |
| `--adopt-current`（不带 `--yes`） | 预览（不改盘） | — | 拒绝：活跃仍是产物且原版已变（加 `--force` 才继续） |
| `--uninstall` | 成功还原 | — | 备份已陈旧，拒绝还原（加 `--force` 才继续） |

---

## 4. 命令速查

```powershell
ntl-builder --update-check                     # 只读检测：0=没变 2=要处理 3=检测失败（不写任何文件）
ntl-builder --adopt-current                    # 预览：会归档哪些旧备份、会采纳哪些新原版（不改盘）
ntl-builder --adopt-current --yes              # 采纳当前原版为新基线（旧备份移进 backup/history/，不删）
ntl-builder --deploy-all --force               # 用新基线重新注入全部章节
ntl-builder --deploy-all --yes                 # 检测到更新时：先采纳、再继续部署（不加 --yes 会停下）
ntl-builder --base-mod none                    # 本次不用整包 mod 的 data.win（整包 mod 还没适配新版时）
ntl-builder --accept-base-drift                # 明知整包基底与游戏版本不匹配仍继续
ntl-builder --no-update-check                  # 跳过前置检测（并行 worker 内部使用）
```

> `--yes` 只代表「我确认这次操作」：它不会跳过归档，也不会删掉任何备份。

---

## 5. 场景演练

### 5.1 Steam 更新了游戏

```text
$ ntl-builder --update-check
  ⛔ 结论：检测到游戏更新（活跃 data.win 已被换回原版 / buildid 变了）
  退出码 2 = 需要处理

$ ntl-builder --adopt-current --yes        # 采纳新原版（旧备份进 backup/history/<buildid>/）
$ ntl-builder --deploy-all --force         # 重新注入
$ ntl-builder --update-check               # 应变成 退出码 0
```

直接跑 `--deploy-all` 而不加 `--yes` 时，它会在**写任何文件之前**停下并打印上面这几步。

### 5.2 游戏更新带来了新章节（例如 `chapter6_windows`）

- `--update-check` → 退出码 2，报告里写 `新章节: chapter6（已登记，等待确认后才注入）`；
- `--deploy-all` 会**跳过**未确认的新章节（打印 `跳过未确认的新章节: chapter6`），其余章节照常；
- 确认要注入：`ntl-builder --adopt-current --yes`（只是登记确认 + 给新章节建立原版备份）→ 再 `--deploy-all`。

### 5.3 整包 `data.win` 型 mod（汉化包等）还没适配新版

整包 mod 的 `ref/data.win` 是绑定游戏版本的。游戏更新后继续用它 = 把本体回退到旧版本，因此检测会拦下：

```powershell
ntl-builder --deploy-all --base-mod none        # 本次只用新原版（该 mod 的字体/文本不会进产物）
ntl-builder --deploy-all --accept-base-drift    # 或者：确认过差异，明知风险继续
```

等 mod 作者更新后，把它的文件换掉，检测会自动认为「已适配当前版本」（记录里那份 `baseModFp` 变了即视为适配）。

### 5.4 想回滚到旧版本

归档都在，直接拷回：

```powershell
Copy-Item Neutraled\backup\history\<buildid>\neutraled\* Neutraled\backup\ -Recurse -Force
ntl-builder --uninstall --force     # 用旧备份还原（会把游戏降级）
```

> `--uninstall` 在检测到「备份陈旧」时会**拒绝**执行（退出码 3），避免一次误操作把游戏降级。

---

## 6. 纪律（不可妥协的三条）

1. **检测只提示**：`--update-check` 不写任何文件（已实测：状态文件 mtime/内容不变）；任何自动「采纳」都必须由用户显式 `--yes` 触发。
2. **旧备份全留**：采纳只把旧备份**移动**进 `backup/history/<buildid>/`，两个备份根（`<游戏根>/backup`、`Neutraled/backup`）各留一份，永不删除。
3. **新章节只登记**：游戏更新后新出现的 `chapterN`，在用户确认前一律不注入。

另外两条工程纪律（踩过坑）：

- **并行 worker 必须显式收到 `--game`**：worker 不带 `--game` 会自己自动探测游戏目录，
  于是 `--deploy-all --game <别的目录>` 的每个子进程都去改**真机游戏**（沙箱隔离测试时真机 `data.win` 被重写过）。
- **`--base-mod` 同样必须转发**：否则每个 worker 按默认顺序挑基底，整包 mod 可能永远选不上。

---

## 7. 故障排查

| 现象 | 原因 / 处置 |
| --- | --- |
| `--update-check` 退出码 3 | 检测本身失败（读不到 acf / 权限）。命令会打印 `[错误] 更新检测失败: …`；`--no-update-check` 可绕过（不推荐） |
| 报告里 `Steam : （未找到 appmanifest_*.acf）` | 非 Steam 安装 / acf 被删：此时只能靠 `data.win` 指纹判更新，章节与本体仍然能判 |
| `--deploy-all` 一直退出码 2 | 检测到更新但没给 `--yes`；按打印的三步执行即可 |
| 检测说没更新，但游戏里 mod 不见了 | 跑 `ntl-builder --update-check`，再看报告里各槽位是不是「产物缺失」→ `--deploy-all` 重建 |
| `backup/history/` 越来越大 | 每次采纳归档一份旧备份（设计如此，**不删**）；确认不再需要旧版本时手动清理对应 `<buildid>` 目录 |
