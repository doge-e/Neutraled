# Neutraled 命令行参考 / CLI reference（自动生成 auto-generated）

> 本文件由 `ntl-builder --gen-docs` 从 `builder/CliCatalogDetails.cs` 生成，**不要手改**。
> Generated from `builder/CliCatalogDetails.cs` by `ntl-builder --gen-docs`; do not edit by hand.
> 改开关说明：改 `CliCatalogDetails.cs` 后重新生成；`--gen-docs --check` 可检测过期。

常用入口 / common entry points: `--deploy` / `--deploy-all` / `--launch root|chapterN|all` / `--help` / `--verify` / `--doctor` / `--deploy-state` / `--restore-session`。

| 开关 / switch | 说明 / description | 需要取值 / takes value | 级别 / level | 类别 / category |
| --- | --- | --- | --- | --- |
| `--asset-guard` | 检查层代码引用的资源名是否存在 — warn when layer code references missing resource names | 是 / yes | 开发 / dev | 资源与取证 / Assets & forensics |
| `--adopt-current` | 把当前原版采纳为新基线 — adopt the current original files as the new baseline |  | 进阶 / advanced | 原版备份 / Vanilla backup |
| `--backup-status` | 只读查看四层备份现状与可重建缓存 — read-only summary of the four backup layers and rebuildable cache |  | 进阶 / advanced | 原版备份 / Vanilla backup |
| `--baseline` | 备份收敛入口（verify/repair/restore/status） — unified entry for vanilla backup verify/repair/restore/status | 是 / yes | 进阶 / advanced | 原版备份 / Vanilla backup |
| `--from` | 修复备份的来源，或配置档复制来源 — backup repair source, or source profile to copy | 是 / yes | 开发 / dev | 原版备份 / Vanilla backup |
| `--repair-backup` | 按可信来源修复原版备份 — repair original backups from a trusted source |  | 常用 / common | 原版备份 / Vanilla backup |
| `--restore-chapter` | 把 live 还原成原版 — restore live chapter files to original |  | 常用 / common | 原版备份 / Vanilla backup |
| `--restore-session` | 按上次启动的部署会话还原 — restore using the last launch deploy session |  | 常用 / common | 原版备份 / Vanilla backup |
| `--update-check` | 只读检测游戏是否更新 — check read-only whether the game updated |  | 常用 / common | 原版备份 / Vanilla backup |
| `--verify-backup` | 只读校验原版备份 — verify original backups read-only |  | 常用 / common | 原版备份 / Vanilla backup |
| `--all-slots` | 对所有存档槽位生效 — apply to every save slot |  | 开发 / dev | B 面 / B-side |
| `--import-bside` | 导入真实 B 面存档到槽位 — import a real B-side save into a slot | 是 / yes | 进阶 / advanced | B 面 / B-side |
| `--make-bside` | 用模板生成 B 面存档 — generate a B-side save from a template |  | 进阶 / advanced | B 面 / B-side |
| `--save-name` | 导入 B 面存档时写入玩家名 — write the player name when importing a B-side save | 是 / yes | 开发 / dev | B 面 / B-side |
| `--slot` | 指定存档槽位 — select the save slot | 是 / yes | 进阶 / advanced | B 面 / B-side |
| `--template-only` | 只导入 B 面模板不落槽位 — import the B-side template without applying it |  | 开发 / dev | B 面 / B-side |
| `--cache-applied` | 查看当前 live 使用了哪份缓存 — show which cache entry is applied to live |  | 开发 / dev | 缓存 / Cache |
| `--cache-check` | 按参数预演缓存是否命中 — preview whether the cache hits for the given target |  | 进阶 / advanced | 缓存 / Cache |
| `--cache-clear` | 清空部署缓存 — clear the deploy cache |  | 进阶 / advanced | 缓存 / Cache |
| `--cache-dedup` | 合并重复的缓存条目 — merge duplicate cache entries |  | 开发 / dev | 缓存 / Cache |
| `--cache-list` | 列出缓存条目与占用 — list cache entries and their sizes |  | 进阶 / advanced | 缓存 / Cache |
| `--cache-max` | 设置启动时缓存上限 MB — set the cache size limit in MB for launch | 是 / yes | 开发 / dev | 缓存 / Cache |
| `--no-cache` | 禁用读取部署缓存 — disable reading the deploy cache |  | 进阶 / advanced | 缓存 / Cache |
| `--chapter` | 指定目标章节 — select the target chapter | 是 / yes | 常用 / common | 章节 / Chapters |
| `--fork` | 复制一个已安装章节作为基底 — copy an installed chapter as the base | 是 / yes | 开发 / dev | 章节 / Chapters |
| `--mode` | 指定新章节的创建模式 — set the creation mode for a new chapter | 是 / yes | 进阶 / advanced | 章节 / Chapters |
| `--new-chapter` | 生成可运行的自定义章节脚手架 — scaffold a runnable custom chapter | 是 / yes | 进阶 / advanced | 章节 / Chapters |
| `--template` | 指定新章节模板 — choose the new chapter template | 是 / yes | 开发 / dev | 章节 / Chapters |
| `--audit-inputscan` | 审计输入扫描结果是否与原逻辑等价 — audit input scan results against the old logic | 是 / yes | 开发 / dev | 调试与开发 / Debug & development |
| `--fast-deploy` | 快速部署（跳过输入重定向；代价：章节内控制台输入屏蔽失效） — fast deploy (skips input redirection; trade-off: in-chapter console input isolation stops working) |  | 开发 / dev | 调试与开发 / Debug & development |
| `--full-deploy` | 强制走完整部署路径 — force the full deploy path |  | 开发 / dev | 调试与开发 / Debug & development |
| `--gml-check` | 按部署规则编译检查层里的 GML — compile-check layer GML with deploy rules |  | 开发 / dev | 调试与开发 / Debug & development |
| `--ignore-unknown` | 忽略未知开关校验 — ignore unknown switch validation |  | 开发 / dev | 调试与开发 / Debug & development |
| `--merge-selftest` | 三方合并自检 — run the three-way merge self-test |  | 开发 / dev | 调试与开发 / Debug & development |
| `--no-launch` | 自测时不启动游戏 — do not launch the game during self-test |  | 开发 / dev | 调试与开发 / Debug & development |
| `--no-save-rename` | 关闭部署时的存档改名 — disable save renaming during deploy |  | 开发 / dev | 调试与开发 / Debug & development |
| `--no-timelines` | 跳过时间线部署，并行 worker 用 — skip timeline deploys for parallel workers |  | 开发 / dev | 调试与开发 / Debug & development |
| `--no-update-check` | 跳过游戏更新检测 — skip the game update check |  | 开发 / dev | 调试与开发 / Debug & development |
| `--probe-code` | 指令级取证对象代码 — inspect object code at instruction level | 是 / yes | 开发 / dev | 调试与开发 / Debug & development |
| `--probe-object` | 对象头部与事件表取证 — inspect object headers and event tables | 是 / yes | 开发 / dev | 调试与开发 / Debug & development |
| `--probe-selftest` | 代码取证工具自检 — self-test the code probe tools |  | 开发 / dev | 调试与开发 / Debug & development |
| `--probe-timeline-runtime` | 只读预检时间线产物运行时 — precheck timeline runtime files without writing |  | 开发 / dev | 调试与开发 / Debug & development |
| `--self-check` | 部署后强制自检产物 — force a self-check of the output after deploy |  | 开发 / dev | 调试与开发 / Debug & development |
| `--selftest` | 端到端自测 — run the end-to-end self-test |  | 进阶 / advanced | 调试与开发 / Debug & development |
| `--send-f2` | 焦点测试时发送 F2 — send F2 during the focus test |  | 开发 / dev | 调试与开发 / Debug & development |
| `--send-keys` | 焦点测试时发送按键序列 — send a key sequence during the focus test | 是 / yes | 开发 / dev | 调试与开发 / Debug & development |
| `--send-text` | 焦点测试时发送文本 — send text during the focus test | 是 / yes | 开发 / dev | 调试与开发 / Debug & development |
| `--verify-refcopy` | 验证跨资源池复制是否保真 — verify that cross-pool copies stay faithful | 是 / yes | 开发 / dev | 调试与开发 / Debug & development |
| `--accept-base-drift` | 整包基底与游戏版本不匹配时仍继续部署 — continue deploying even if the base mod data.win does not match the game build |  | 开发 / dev | 部署 / Deploying |
| `--base-mod` | 指定整包 mod 作为部署基底 — choose the whole-package mod used as deploy base |  | 进阶 / advanced | 部署 / Deploying |
| `--deploy` | 部署 root 或指定章节 — deploy root or the chosen chapter |  | 常用 / common | 部署 / Deploying |
| `--deploy-all` | 并行部署全部目标 — deploy every target in parallel |  | 常用 / common | 部署 / Deploying |
| `--force-timelines` | 强制重新部署时间线产物 — force redeploy of timeline outputs |  | 开发 / dev | 部署 / Deploying |
| `--jobs` | 设置并行部署的进程数 — set the number of parallel deploy workers | 是 / yes | 开发 / dev | 部署 / Deploying |
| `--no-root` | 指定章节时只部署该章不连带 root — deploy only the chosen chapter without root |  | 进阶 / advanced | 部署 / Deploying |
| `--export-packs` | 导出可叠加的资源包 — export resource packs that can be layered | 是 / yes | 进阶 / advanced | 导出资源 / Exporting assets |
| `--export-shaders` | 导出色差着色器资源包 — export shader resources used for color grading | 是 / yes | 开发 / dev | 导出资源 / Exporting assets |
| `--map-out` | 指定地图或资源输出目录 — set the output directory for maps or assets | 是 / yes | 开发 / dev | 导出资源 / Exporting assets |
| `--add-external` | 把成品 exe 注册为外部章节 — register a standalone exe as an external chapter | 是 / yes | 进阶 / advanced | 外部章节 / External chapters |
| `--ensure-watcher` | 守候进程没在跑就静默启动 — start the watcher silently if it is not running |  | 进阶 / advanced | 外部章节 / External chapters |
| `--ext-args` | 传给外部引擎的启动参数 — extra arguments passed to the external engine | 是 / yes | 开发 / dev | 外部章节 / External chapters |
| `--focus-test` | 测试后台进程能否抢到游戏焦点 — test whether a background process can grab game focus | 是 / yes | 开发 / dev | 外部章节 / External chapters |
| `--keep` | 焦点测试后保留游戏进程 — keep the game process after the focus test |  | 开发 / dev | 外部章节 / External chapters |
| `--watch-autostart` | 管理守候进程自启 — manage watcher autostart |  | 进阶 / advanced | 外部章节 / External chapters |
| `--watch-external` | 静默守候并拉起外部引擎 — run the silent watcher for external engines |  | 进阶 / advanced | 外部章节 / External chapters |
| `--chars` | 指定字体字表的字符集合 — set the explicit character list for the font | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--charset` | 指定中文字体的字符集范围 — choose the character set used to build the CJK font | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--font-name` | 指定生成的字体资源名 — set the resource name of the generated font | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--font-native` | 把本机字形覆盖进字体包 — merge local glyphs into the font package |  | 进阶 / advanced | 字体 / Fonts |
| `--font-probe` | 查看 data.win 里各字体的字形数 — show glyph counts for fonts in a data.win |  | 进阶 / advanced | 字体 / Fonts |
| `--font-source` | 指定本机字形来源 — choose the local glyph source | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--from-font` | 从已有 data.win 搬运字形 — copy glyphs from an existing data.win | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--make-cjk-font` | 生成中文字体包 — build a CJK font package |  | 进阶 / advanced | 字体 / Fonts |
| `--size` | CJK 字号，或队列条目字节数 — font pixel size for CJK builds, or queue entry byte size | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--source-font` | 指定搬运字形时的源字体 — set the source font when copying glyphs | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--ttf` | 指定生成字体用的 ttf — set the ttf used to build the font | 是 / yes | 开发 / dev | 字体 / Fonts |
| `--as-delta` | 强制按 xdelta 补丁导入（保留旧 --fetch 行为） — force xdelta import (preserves the old --fetch behavior) |  | 开发 / dev | GameBanana / GameBanana |
| `--fetch` | 旧名（已弃用）：请改用 --install-id — legacy alias (deprecated): use --install-id | 是 / yes | 开发 / dev | GameBanana / GameBanana |
| `--fetch-mod` | 旧名（已弃用）：请改用 --install-id（默认取最大文件） — legacy alias (deprecated): use --install-id (picks the largest file by default) | 是 / yes | 进阶 / advanced | GameBanana / GameBanana |
| `--file` | 1-based 文件序号（--install-id / --gb-install） — 1-based file index (--install-id / --gb-install) |  | 开发 / dev | GameBanana / GameBanana |
| `--gb-files` | 列出 GameBanana 条目的文件 — list files of a GameBanana entry |  | 进阶 / advanced | GameBanana / GameBanana |
| `--gb-install` | 旧名（已弃用）：请改用 --install-id — legacy alias (deprecated): use --install-id |  | 进阶 / advanced | GameBanana / GameBanana |
| `--gb-search` | 旧名（已弃用）：请改用 --search — legacy alias (deprecated): use --search |  | 进阶 / advanced | GameBanana / GameBanana |
| `--gb-type` | 指定 GameBanana 条目类型 — set the GameBanana entry type |  | 开发 / dev | GameBanana / GameBanana |
| `--install-id` | 统一入口：按 GameBanana 条目下载并自动路由导入 — unified entry: download a GameBanana entry and route it to the right importer | 是 / yes | 常用 / common | GameBanana / GameBanana |
| `--install-url` | 统一入口：从直链下载并自动路由导入 — unified entry: download from a direct URL and route it to the right importer | 是 / yes | 进阶 / advanced | GameBanana / GameBanana |
| `--model` | 指定 GameBanana 条目类型 Mod 或 Wip — set the GameBanana model to Mod or Wip | 是 / yes | 开发 / dev | GameBanana / GameBanana |
| `--no-blocklist` | 搜索时不过滤 GameBanana 黑名单 — do not filter the GameBanana blocklist when searching |  | 进阶 / advanced | GameBanana / GameBanana |
| `--per-page` | 搜索每页结果数（1..100，默认 15） — search results per page (1..100, default 15) | 是 / yes | 进阶 / advanced | GameBanana / GameBanana |
| `--route-probe` | 只读探测本地包类型并给出导入器建议 — read-only probe of a local package with an importer recommendation | 是 / yes | 进阶 / advanced | GameBanana / GameBanana |
| `--search` | 统一搜索入口（默认过滤黑名单；--dry-run 零网络） — unified search entry (blocklist on by default; --dry-run uses no network) | 是 / yes | 进阶 / advanced | GameBanana / GameBanana |
| `--as` | 强制包型（xdelta|datawin|ntlmod|kristal|gm3p|external） — force the package kind (xdelta|datawin|ntlmod|kristal|gm3p|external) | 是 / yes | 开发 / dev | 导入 mod / Importing mods |
| `--base` | 指定差异层或资源包比较的基线 — set the baseline data.win for layers or packs | 是 / yes | 开发 / dev | 导入 mod / Importing mods |
| `--diff-override` | 差异层冲突时后写的覆盖先写的 — let later layers win when diff layers conflict |  | 开发 / dev | 导入 mod / Importing mods |
| `--diff-verbose` | 输出差异层的详细处理日志 — print verbose diff layer processing logs |  | 开发 / dev | 导入 mod / Importing mods |
| `--extract-diff` | 把整包 mod 提取成可叠加差异层 — extract a whole-package mod into a diff layer | 是 / yes | 进阶 / advanced | 导入 mod / Importing mods |
| `--import-all` | 并行导入全部目标 — import every target in parallel |  | 进阶 / advanced | 导入 mod / Importing mods |
| `--import-delta` | 导入 xdelta 或修改版 data.win — import an xdelta or modified data.win | 是 / yes | 进阶 / advanced | 导入 mod / Importing mods |
| `--import-mod` | 导入 mod 包并自动识别格式 — import a mod package with auto format detection | 是 / yes | 常用 / common | 导入 mod / Importing mods |
| `--layer-from-base` | 用反编译差异生成源码级差异层 — build a source-level diff layer by decompiling | 是 / yes | 开发 / dev | 导入 mod / Importing mods |
| `--no-timeline` | 导入时不生成时间线 — skip timeline generation when importing |  | 开发 / dev | 导入 mod / Importing mods |
| `--convert-kristal` | 把 Kristal mod 转换成 Neutraled 格式 — convert a Kristal mod into Neutraled format | 是 / yes | 进阶 / advanced | Kristal / Kristal |
| `--import-kristal` | 导入 Kristal 工程或 mod — import a Kristal project or mod | 是 / yes | 进阶 / advanced | Kristal / Kristal |
| `--import-kristal-map` | 导入 Kristal 的 Tiled 地图 — import a Tiled map from Kristal | 是 / yes | 开发 / dev | Kristal / Kristal |
| `--kristal-console` | 给融合版 Kristal exe 注入控制台 — inject the console into a fused Kristal exe |  | 进阶 / advanced | Kristal / Kristal |
| `--kristal-console-restore` | 从备份还原融合版 exe — restore a fused exe from its backup |  | 开发 / dev | Kristal / Kristal |
| `--kristal-ensure` | 只在控制台缺失或过期时注入 — inject only when the console is missing or stale |  | 开发 / dev | Kristal / Kristal |
| `--kristal-merge` | 把插件合并进 Kristal 宿主 — merge plugins into a Kristal host project | 是 / yes | 开发 / dev | Kristal / Kristal |
| `--map-id` | 指定导入地图的 id — set the id of the imported map | 是 / yes | 开发 / dev | Kristal / Kristal |
| `--validate-kristal` | 校验 Kristal mod 是否合规 — validate a Kristal mod | 是 / yes | 开发 / dev | Kristal / Kristal |
| `--with` | 指定合并进宿主的插件目录 — set plugin directories to merge into the host | 是 / yes | 开发 / dev | Kristal / Kristal |
| `--lang` | 设置输出语言 — set the output language | 是 / yes | 常用 / common | 语言 / Language |
| `--lang-coverage` | 统计语言包覆盖率 — report language pack coverage |  | 进阶 / advanced | 语言 / Language |
| `--lang-list` | 列出可用界面语言 — list available interface languages |  | 进阶 / advanced | 语言 / Language |
| `--lang-migrate` | 把旧 gui_lang.txt 并进 config.json 的 lang 并归档 — merge legacy gui_lang.txt into config.json lang and archive it |  | 进阶 / advanced | 语言 / Language |
| `--lang-resolve` | 显示语言从哪来（--lang/NTL_LANG/config/默认） — show where the language comes from (--lang, NTL_LANG, config, default) |  | 进阶 / advanced | 语言 / Language |
| `--lang-template` | 导出待翻译的语言模板 — export a translation template |  | 开发 / dev | 语言 / Language |
| `--lang-use` | 切换界面语言并写入配置 — switch the interface language and save it |  | 进阶 / advanced | 语言 / Language |
| `--author` | 指定 mod 或章节的作者名 — set the author name for a mod or chapter | 是 / yes | 进阶 / advanced | mod 管理 / Mod management |
| `--conflicts` | 只检查 mod 冲突不部署 — only check mod conflicts without deploying |  | 常用 / common | mod 管理 / Mod management |
| `--mod-force` | 校验和不符时仍强制继续 — continue even if a checksum does not match |  | 开发 / dev | mod 管理 / Mod management |
| `--mod-install` | 安装原生 Neutraled 或 Kristal mod 包 — install a native Neutraled or Kristal mod package | 是 / yes | 进阶 / advanced | mod 管理 / Mod management |
| `--mod-list` | 列出已安装 mod（加 --json 输出稳定结构） — list installed mods (--json for machine-readable output) |  | 常用 / common | mod 管理 / Mod management |
| `--name` | 覆盖 mod 或包内名称 — override the mod or package name | 是 / yes | 进阶 / advanced | mod 管理 / Mod management |
| `--new-mod` | 生成新 mod 的脚手架 — scaffold a new mod | 是 / yes | 常用 / common | mod 管理 / Mod management |
| `--pack` | 把 mod 打包成 ntlmod — pack a mod into an ntlmod file | 是 / yes | 进阶 / advanced | mod 管理 / Mod management |
| `--frontend-matrix` | 四前端（CLI/GUI/Studio/Web）能力矩阵 — capability matrix for CLI/GUI/Studio/Web |  | 进阶 / advanced | 平台 / Platform |
| `--platform-info` | 查看平台能力与可写性 — show platform capabilities and writability |  | 进阶 / advanced | 平台 / Platform |
| `--plugin-disable` | 禁用插件 — disable a plugin |  | 进阶 / advanced | 插件 / Plugins |
| `--plugin-enable` | 启用插件 — enable a plugin |  | 进阶 / advanced | 插件 / Plugins |
| `--plugin-hooks` | 列出插件注册的钩子 — list hooks registered by plugins |  | 开发 / dev | 插件 / Plugins |
| `--plugin-info` | 查看插件详情 — show details of a plugin |  | 开发 / dev | 插件 / Plugins |
| `--plugin-install` | 安装插件 — install a plugin |  | 进阶 / advanced | 插件 / Plugins |
| `--plugin-list` | 列出插件 — list plugins |  | 进阶 / advanced | 插件 / Plugins |
| `--plugin-remove` | 卸载插件 — remove a plugin |  | 进阶 / advanced | 插件 / Plugins |
| `--desc` | 配置档的说明文字 — set the description of a profile |  | 开发 / dev | 配置档 / Profiles |
| `--profile-copy` | 复制配置档 — copy a profile |  | 开发 / dev | 配置档 / Profiles |
| `--profile-delete` | 删除配置档 — delete a profile |  | 开发 / dev | 配置档 / Profiles |
| `--profile-export` | 导出配置档 — export a profile |  | 开发 / dev | 配置档 / Profiles |
| `--profile-import` | 导入配置档 — import a profile |  | 开发 / dev | 配置档 / Profiles |
| `--profile-list` | 列出配置档（加 --json 输出稳定结构） — list profiles (--json for machine-readable output) |  | 进阶 / advanced | 配置档 / Profiles |
| `--profile-new` | 新建配置档 — create a profile |  | 进阶 / advanced | 配置档 / Profiles |
| `--profile-rename` | 重命名配置档 — rename a profile |  | 开发 / dev | 配置档 / Profiles |
| `--profile-show` | 查看配置档内容 — show the contents of a profile |  | 进阶 / advanced | 配置档 / Profiles |
| `--profile-use` | 应用配置档 — apply a profile |  | 进阶 / advanced | 配置档 / Profiles |
| `--delete-after` | 队列条目安装成功后删除下载文件 — delete the download after a queue item installs |  | 开发 / dev | 下载队列 / Download queue |
| `--delete-file` | 移除队列条目时一并删除文件 — also delete the downloaded file when removing a queue item |  | 开发 / dev | 下载队列 / Download queue |
| `--no-install` | 队列只下载不安装 — download only without installing for queue runs |  | 开发 / dev | 下载队列 / Download queue |
| `--queue-add` | 把下载加入队列 — add a download to the queue |  | 进阶 / advanced | 下载队列 / Download queue |
| `--queue-list` | 列出下载队列 — list the download queue |  | 进阶 / advanced | 下载队列 / Download queue |
| `--queue-remove` | 从队列移除条目 — remove an entry from the queue |  | 开发 / dev | 下载队列 / Download queue |
| `--queue-run` | 执行下载队列 — run the download queue |  | 进阶 / advanced | 下载队列 / Download queue |
| `--url` | 队列条目的下载地址 — set the download url of a queue entry |  | 开发 / dev | 下载队列 / Download queue |
| `--no-backup` | 建恢复点时只存清单不复制文件 — store only the manifest when creating a restore point |  | 开发 / dev | 整游戏恢复点 / Restore points |
| `--profile` | 建恢复点时指定来源配置档 — choose the source profile for a restore point |  | 开发 / dev | 整游戏恢复点 / Restore points |
| `--restore-apply` | 恢复整游戏恢复点 — apply a whole-game restore point |  | 进阶 / advanced | 整游戏恢复点 / Restore points |
| `--restore-create` | 创建整游戏恢复点 — create a whole-game restore point |  | 进阶 / advanced | 整游戏恢复点 / Restore points |
| `--restore-delete` | 删除恢复点 — delete a restore point |  | 开发 / dev | 整游戏恢复点 / Restore points |
| `--restore-export` | 导出恢复点文件 — export a restore point file |  | 开发 / dev | 整游戏恢复点 / Restore points |
| `--restore-import` | 导入恢复点文件 — import a restore point file |  | 开发 / dev | 整游戏恢复点 / Restore points |
| `--restore-list` | 列出恢复点 — list restore points |  | 进阶 / advanced | 整游戏恢复点 / Restore points |
| `--link-saves` | 把存档目录联接起来 — link the save directory | 是 / yes | 进阶 / advanced | 存档 / Saves |
| `--repair-saves` | 修复悬空的存档联接 — repair dangling save links |  | 进阶 / advanced | 存档 / Saves |
| `--restore-save` | 从备份还原某个存档槽 — restore a save slot from backup |  | 进阶 / advanced | 存档 / Saves |
| `--save-list` | 列出存档槽 — list save slots |  | 进阶 / advanced | 存档 / Saves |
| `--block-add` | 向 GameBanana 黑名单加入条目 — add an entry to the GameBanana blacklist |  | 开发 / dev | 安全与拦截 / Safety & blocking |
| `--block-list` | 列出 GameBanana 黑名单 — list the GameBanana blacklist |  | 开发 / dev | 安全与拦截 / Safety & blocking |
| `--block-remove` | 从 GameBanana 黑名单移除条目 — remove an entry from the GameBanana blacklist |  | 开发 / dev | 安全与拦截 / Safety & blocking |
| `--kind` | 黑名单条目的匹配类型 — set the blacklist entry match kind |  | 开发 / dev | 安全与拦截 / Safety & blocking |
| `--snapshot-auto` | 给所有 mod 建保底快照 — snapshot every mod as a fallback |  | 开发 / dev | 版本快照 / Snapshots |
| `--snapshot-create` | 给当前 mod 内容打快照 — snapshot the current mod content |  | 进阶 / advanced | 版本快照 / Snapshots |
| `--snapshot-delete` | 删除版本快照 — delete a version snapshot |  | 开发 / dev | 版本快照 / Snapshots |
| `--snapshot-import` | 导入快照 — import a snapshot |  | 开发 / dev | 版本快照 / Snapshots |
| `--snapshot-list` | 列出 mod 的版本快照 — list version snapshots of a mod |  | 进阶 / advanced | 版本快照 / Snapshots |
| `--snapshot-use` | 切换到某个快照 — switch to a snapshot |  | 进阶 / advanced | 版本快照 / Snapshots |
| `--force` | 忽略缓存与已有产物强制重做 — force redo even when caches or outputs exist |  | 常用 / common | 入门 / Getting started |
| `--game` | 指定游戏根目录 — set the game root directory | 是 / yes | 进阶 / advanced | 入门 / Getting started |
| `--help` | 显示帮助 — show help |  | 常用 / common | 入门 / Getting started |
| `--install` | 把 Neutraled 安装进游戏目录 — install Neutraled into the game directory |  | 常用 / common | 入门 / Getting started |
| `--launch` | 部署后走 Steam 启动游戏（裸 --launch = root；可跟章节位置值） — deploy then launch the game through Steam (bare --launch = root; optional chapter) |  | 常用 / common | 入门 / Getting started |
| `--no-start` | 只部署不拉起 Steam（与 --launch 搭配；Studio/自动化用） — deploy only, do not launch Steam (pair with --launch; used by Studio/automation) |  | 进阶 / advanced | 入门 / Getting started |
| `--src` | 指定安装内容目录 — set the install source directory | 是 / yes | 开发 / dev | 入门 / Getting started |
| `--uninstall` | 从游戏目录卸载 Neutraled — uninstall Neutraled from the game directory |  | 常用 / common | 入门 / Getting started |
| `--version` | 显示构建器版本 — show the builder version |  | 常用 / common | 入门 / Getting started |
| `--yes` | 对确认提示一律回答是 — answer yes to confirmation prompts |  | 常用 / common | 入门 / Getting started |
| `--theme-import` | 导入主题 — import a theme |  | 开发 / dev | 主题 / Themes |
| `--theme-list` | 列出主题 — list themes |  | 进阶 / advanced | 主题 / Themes |
| `--theme-show` | 查看主题内容 — show the contents of a theme |  | 开发 / dev | 主题 / Themes |
| `--theme-use` | 应用主题 — apply a theme |  | 进阶 / advanced | 主题 / Themes |
| `--api-doc` | 生成 API 参考文档 — generate the API reference docs |  | 开发 / dev | 工具 / Tools |
| `--check` | 只比较生成的文档是否过期，不写盘 — check whether generated docs are stale without writing |  | 开发 / dev | 工具 / Tools |
| `--config-get` | 查看 config.json 一个键的生效值与来源 — show the effective value and source of one config key | 是 / yes | 进阶 / advanced | 工具 / Tools |
| `--config-keys` | 列出 config.json 全部键（类型/owner/默认值/废弃） — list all config.json keys (type, owner, default, deprecated) |  | 进阶 / advanced | 工具 / Tools |
| `--config-migrate` | 把废弃配置键迁移成规范键（旧键保留兼容） — migrate deprecated config keys to canonical ones (old keys kept) |  | 进阶 / advanced | 工具 / Tools |
| `--config-set` | 读-改-写 config.json 的一个键 — read-modify-write a single key in config.json | 是 / yes | 进阶 / advanced | 工具 / Tools |
| `--dry-run` | 只分析不写盘 — analyze only without writing anything |  | 进阶 / advanced | 工具 / Tools |
| `--dump` | 导出指定代码条目的反编译结果 — dump the decompiled code for one entry | 是 / yes | 开发 / dev | 工具 / Tools |
| `--dumpall` | 导出全部代码条目 — dump every code entry | 是 / yes | 开发 / dev | 工具 / Tools |
| `--extract` | 解压 zip、7z、rar 等压缩包 — extract zip, 7z, rar and other archives | 是 / yes | 开发 / dev | 工具 / Tools |
| `--find` | 搜索引用某符号的代码 — find code that references a symbol | 是 / yes | 开发 / dev | 工具 / Tools |
| `--gameinfo` | 显示游戏版本与章节信息 — show game version and chapter information |  | 开发 / dev | 工具 / Tools |
| `--gen-docs` | 生成命令行参考文档 — generate the CLI reference documentation |  | 开发 / dev | 工具 / Tools |
| `--json` | 以 JSON 输出机器可读结果 — print machine-readable JSON output |  | 进阶 / advanced | 工具 / Tools |
| `--list` | 列出匹配的代码条目 — list matching code entries | 是 / yes | 开发 / dev | 工具 / Tools |
| `--listobjs` | 列出对象资源 — list object resources | 是 / yes | 开发 / dev | 工具 / Tools |
| `--listrooms` | 列出房间资源 — list room resources |  | 开发 / dev | 工具 / Tools |
| `--listsprites` | 列出精灵资源 — list sprite resources | 是 / yes | 开发 / dev | 工具 / Tools |
| `--make-icon` | 生成程序图标 — generate the application icon |  | 开发 / dev | 工具 / Tools |
| `--note` | 给快照或黑名单条目加备注 — add a note to a snapshot or blacklist entry |  | 开发 / dev | 工具 / Tools |
| `--out` | 指定输出文件或目录 — set the output file or directory | 是 / yes | 进阶 / advanced | 工具 / Tools |
| `--room` | 按名字筛选房间 — filter rooms by name | 是 / yes | 开发 / dev | 工具 / Tools |
| `--sprite` | 查看精灵详情 — show details of a sprite | 是 / yes | 开发 / dev | 工具 / Tools |
| `--content-check` | 检查游戏文本是否被产物字体覆盖 — check that output fonts cover all displayed text |  | 进阶 / advanced | 校验与自检 / Verify & self-check |
| `--deploy-state` | 查询 live 是原版还是已部署 — report whether live is original or deployed |  | 常用 / common | 校验与自检 / Verify & self-check |
| `--doctor` | 运行环境与依赖自检 — run environment and dependency self-checks |  | 常用 / common | 校验与自检 / Verify & self-check |
| `--info` | 显示 data.win 概要 — show a summary of a data.win |  | 常用 / common | 校验与自检 / Verify & self-check |
| `--lint` | 静态规则检查 — run static rule checks |  | 常用 / common | 校验与自检 / Verify & self-check |
| `--smoke` | API 冒烟测试 — run the API smoke test |  | 常用 / common | 校验与自检 / Verify & self-check |
| `--verify` | 校验注入结果 — verify the injected result |  | 常用 / common | 校验与自检 / Verify & self-check |
| `--auto-stop` | 网页界面空闲时自动退出 — stop the web UI automatically when idle |  | 开发 / dev | 网页界面 / Web UI |
| `--no-open` | 启动网页界面时不自动开浏览器 — do not open a browser when starting the web UI |  | 开发 / dev | 网页界面 / Web UI |
| `--web` | 启动本地网页界面 — start the local web interface |  | 进阶 / advanced | 网页界面 / Web UI |

