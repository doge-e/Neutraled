# 网页界面（WEBUI）

`--web` 起一个零依赖的本地网页控制台：只绑 `127.0.0.1`，用浏览器管理 mod / 配置档 / 快照 / 恢复点 / GameBanana / 下载队列 / 插件 / 主题 / 语言 / 部署 —— 和命令行走同一条代码路径。

> 姊妹文档：MANAGE.md §10 是速查表，本文是详解版（启动参数、22 条 JSON 接口、安全边界、实测输出、已知限制）。
> 零依赖：不引任何 CDN 与前端框架，页面就是 `Neutraled/web/index.html` + `app.js` + `style.css`，断网也能打开；只有 GameBanana 搜索 / 下载需要联网。

---

## 1. 它解决什么问题

命令行做一次操作要敲一长串开关，看结果还得翻 `--xxx-list` 的文本输出。网页界面把日常操作变成点按钮：

- 一次开关很多 mod，每一项的章节、作者、启用状态都看得见；
- 配置档 / 快照 / 恢复点先看清楚内容，再决定套用哪一个；
- 边看 GameBanana 搜索结果边装；
- 换主题、换语言后立刻看到新配色与译文覆盖率。

它**不是**：远程管理面板（只绑本机、没有登录）、mod 编辑器、命令行的替代品。网页里没有的操作（例如 `--adapt`、`--pack`、插件安装/卸载）仍然要回到命令行。

## 2. 启动

```powershell
ntl-builder --web                                   # 默认端口 7931，并自动打开浏览器
ntl-builder --web --port 8000                       # 换端口
ntl-builder --web --no-open                         # 不自动开浏览器
ntl-builder --web --no-open --port 7933 --auto-stop # 空闲自动退出（脚本 / 短测用）
```

也可以直接跑发布出来的 exe（Windows 上加不加 `.exe` 都行）：

```powershell
& 'E:\steam\steamapps\common\DELTARUNE\Neutraled\builder\bin\Release\net9.0\ntl-builder.exe' --web --no-open --port 7933 --auto-stop
```

真实输出（实测，工作目录 = 游戏根）：

```
游戏根: E:\steam\steamapps\common\DELTARUNE
[语言] 已加载 7 个外部语言包: de, es, fr, ja, ko, ru, zh-tw（共 15715 条译文）
[Web] 网页界面已启动: http://127.0.0.1:7933/
[Web] 游戏根: E:\steam\steamapps\common\DELTARUNE
[Web] 静态目录: E:\steam\steamapps\common\DELTARUNE\Neutraled\web
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `--port <n>` | `7931` | 起始端口；被占用就 +1 重试，最多 6 个端口（`port`..`port+5`） |
| `--no-open` | 关（默认会开浏览器） | 不调用系统浏览器；适合远程终端 / 脚本 |
| `--auto-stop` | 关 | 空闲看门狗：空闲超过设定时间没有请求就自己退出（默认 120 秒） |

环境变量 `NTL_WEB_IDLE_MS` 覆盖 `--auto-stop` 的空闲毫秒数，有效范围 200..86400000（小于 200 或超过一天按默认 120 秒算）。

### 2.1 只监听回环地址

服务只绑 `http://127.0.0.1:<port>/`；若被 URL ACL 之类拒绝，会退一格试 `http://localhost:<port>/`（http.sys 对 localhost 更宽松）。实测确认：

```powershell
Get-NetTCPConnection -LocalPort 7933 -State Listen | Select-Object LocalAddress,LocalPort,State
```

```
LocalAddress LocalPort  State
------------ ---------  -----
127.0.0.1         7933 Listen
```

只有回环地址，没有 `0.0.0.0`，局域网里的其它机器连不上。每个请求还会再验一次来源（`req.IsLocal` 且对端地址必须是回环地址），非本机直接 403 `{"error":"只允许本机访问"}`。

### 2.2 端口被占用 → 自动 +1（实测）

先以 `--port 7933` 起一个服务，再用同一个端口起第二个：

```
[Web] 端口 7933 被占用，已改用 7934
[Web] 网页界面已启动: http://127.0.0.1:7934/
```

`port`..`port+5` 六个端口全被占住时（用 6 个 TcpListener 占住 7933..7938 复现）：

```
[Web] 无法绑定端口 7933-7938: 另一个程序正在使用此文件，进程无法访问。
```

进程退出码 1。

### 2.3 `--auto-stop` 的真实语义：空闲看门狗

它不是「启动后立刻返回」，而是照常服务、空闲到点自己停：

```powershell
$env:NTL_WEB_IDLE_MS = '1500'    # 演示用：1.5 秒空闲
ntl-builder --web --no-open --port 7933 --auto-stop
```

```
[Web] 网页界面已启动: http://127.0.0.1:7933/
[Web] 游戏根: E:\steam\steamapps\common\DELTARUNE
[Web] 静态目录: E:\steam\steamapps\common\DELTARUNE\Neutraled\web
[Web] 空闲 1 秒无请求，网页界面自动关闭
[Web] 网页界面已停止
```

实测整段耗时约 3.3 秒（含启动），退出码 0；不设 `NTL_WEB_IDLE_MS` 时按默认 120 秒算。

### 2.4 退出码

| 返回码 | 含义 |
| --- | --- |
| 0 | 正常结束：`POST /api/shutdown`、Ctrl+C，或 `--auto-stop` 空闲到点 |
| 1 | 端口绑定失败（`port`..`port+5` 全占用，或无权限绑定） |
| 2 | 游戏根无效（目录不存在） |
| 3 | 已经在运行（同一进程里重复调用 `WebUi.Start`） |

实测：正常 `--auto-stop` 退出 → `EXITCODE=0`；六个端口全占 → `EXITCODE=1`。

### 2.5 怎么关掉它

三种方式效果一样（都会打印 `[Web] 网页界面已停止` 并释放端口）：

1. 页面页脚的「关闭服务」按钮 → `POST /api/shutdown`，实测返回 `{"ok":true,"message":"正在关闭"}`；
2. 在跑服务的控制台按 Ctrl+C；
3. 用 `--auto-stop` 起的服务，空闲到点自己退出。

想确认端口已经放开：

```powershell
Get-NetTCPConnection -LocalPort 7933 -State Listen -ErrorAction SilentlyContinue
```

## 3. 安全边界

| 项 | 现状 |
| --- | --- |
| 绑定 | 只绑 `127.0.0.1`（退路 `localhost`），不监听 `0.0.0.0` |
| 来源校验 | 每请求校验 `IsLocal` + 回环地址，非本机 403 |
| 鉴权 | **没有**登录 / Cookie / token：能在这台机器上发 HTTP 的程序就能调所有接口 |
| 请求体上限 | 1 MB（`MaxBody = 1024*1024`） |
| 标识校验 | 所有 id（mod id、版本、配置档、恢复点、章节、主题、语言码）走 `SafeToken`：拒绝空、超 96 字符、`..`、`/`、`\`，以及文件名字符；非法 → 400 |
| 静态文件 | 只允许 `web/` 目录内的文件：含 `..` 直接拒，解析后的绝对路径还必须落在 `web/` 前缀内；响应带 `Cache-Control: no-store` |
| 页面注入 | `app.js` 一律用 `textContent` 写服务端字符串，不用 `innerHTML` |
| 破坏性操作 | 前端 `window.confirm` 二次确认；接口本身不做「危险确认」（命令行的 `--force` 对应请求体里的 `force` 字段） |

> ⚠ **别把它当安全的远程面板**：没有鉴权、没有 HTTPS、没有 CSRF token，它的安全模型只有一条 —— 「只有本机能连」。要在多台机器上用，请自己在外面套代理 + 认证，或者继续用命令行。

## 4. 界面：十个面板

页脚有「刷新 / 关闭服务」，顶栏三个 chip 显示平台、游戏根、当前语言；页面每 15 秒自动刷新一次状态。

| 面板 | 做什么 | 主要接口 |
| --- | --- | --- |
| 总览 | 游戏根 / 平台 / 版本、mod 计数、当前配置档与语言；两个部署快捷键 | `/api/status`、`/api/deploy` |
| mod 管理 | 列出所有 mod（含已禁用），逐个开关 | `/api/mods`、`/api/mods/toggle` |
| 配置档 | 套用 / 把当前状态存进配置档 / 新建 | `/api/profiles`、`/api/profile/use`、`/api/profile/save` |
| 快照 | 按 mod 看版本快照并回切 | `/api/snapshots`、`/api/snapshot/use` |
| 恢复点 | 列恢复点并恢复 | `/api/restore`、`/api/restore/apply` |
| 下载 | 搜 GameBanana → 安装；跑下载队列 | `/api/gb/search`、`/api/gb/install`、`/api/queue`、`/api/queue/run` |
| 插件 | 只读列出插件与状态 | `/api/plugins` |
| 主题 | 看色板、切换主题 | `/api/themes`、`/api/theme/use` |
| 语言 | 切换语言、看译文覆盖率 | `/api/lang`、`/api/lang/use`、`/api/lang/coverage` |
| 部署 | 选章节后部署 | `/api/deploy` |

### 4.1 总览

显示游戏根、平台、版本、mod 总数 / 启用数、当前配置档、语言、主题，并提供「部署 root」「部署当前章节」两个快捷键（当前章节由状态里的章节后缀推断）。

### 4.2 mod 管理

表格列出每个 mod 的 id、名称、作者、版本、章节与开关。开关直接生效（**不弹确认**），成功 toast「已启用 X / 已禁用 X」；启用状态写回该 mod 自己的 `mod.json`，和命令行 `--enable/--disable` 是同一处数据。

### 4.3 配置档

- 「套用」弹确认：`确认套用配置档「<id>」？` / `当前的 mod 启用状态会被改写。`；
- 「保存进配置档」弹确认：`把当前的 mod 启用状态保存进配置档「<name|id>」？` / `该档原有记录会被覆盖。`；
- 「新建配置档」表单（id + 名称）+「从当前状态创建」按钮：id 为空时只提示「请先填 id」，不会发请求。

### 4.4 快照

下拉可切「（全部 mod）」或单个 mod；表格列 modId / 版本 / 名称 / 作者 / 创建时间 / 来源 / 文件·体积 / 操作。「回切到此版本」弹确认：`确认把 mod「<modId>」回切到版本 <version>？` / `现有文件会被快照覆盖。`

### 4.5 恢复点

表格列 id / 名称 / 创建时间 / 来源 / 配置档 / 体积 / 数据（`含数据` 或 `仅清单`）/ 操作。「恢复」弹确认：`确认恢复恢复点「<id>」？` / `这会覆盖 dist/ 与游戏数据文件。`；对不含数据备份的恢复点，后半句换成 `该恢复点不含数据备份，只会还原清单。`

> 恢复点会动 `dist/` 与游戏数据文件；操作前请确认游戏已关闭（和命令行 `--restore-apply` 的注意事项一致，见 MANAGE.md §13）。

### 4.6 下载（GameBanana）

搜索框（支持回车）→「搜索」；「安装到章节」下拉默认当前章节。结果表格列 id / 名称 / 作者 / 下载量 / 简介（截断 160 字）/「安装」，点安装会弹：`确认安装「<name>」到 <chapter>？` / `安装过程会联网下载并写入 mods/ 目录。`

队列区显示每项的 id / mod / 文件 / 状态 / 进度 / 错误（含 fail、error、失败字样的状态标红），两个按钮：

- 「执行队列（下载并安装）」→ `POST /api/queue/run {autoInstall:true}`；
- 「执行队列（仅下载）」→ `{autoInstall:false}`。

两者都会弹确认。队列本身是在命令行 `--dl-add`、`--gb-install` 累积出来的，网页只是把它们跑完。

### 4.7 插件

只读：列出插件 id、名称、版本、作者、状态、权限与诊断信息，状态含 error / fail / invalid 的标红。页面上的说明写着「插件目录：Neutraled/plugins/<id>/plugin.json；这里只读展示，安装/卸载请用命令行。」

### 4.8 主题

每个主题显示名称与最多 8 个色块；活动主题标「使用中」，其它有「应用」按钮。切换后**命令行与网页同时换色**：颜色取自主题 `colors` 字典，逐键写进网页的 CSS 变量，缺项自动回退到内置配色；`fontFamily` / `fontSize` 同理。

### 4.9 语言

下拉由「可用语言包 + 内置语言 + 当前语言」合并而成，内置的标「（内置）」。

- 「切换」写 `config.json` 的 `lang` 并加载 `Neutraled/lang/lang_<code>.json`；
- 「查看译文覆盖率」把覆盖率报告（纯文本）原样显示在页面的报告框里，标题带返回码。

### 4.10 部署

选章节 →「开始部署」→ 结果写进页面输出区。它调用主程序注入的部署钩子，与命令行 `--deploy` 是同一套逻辑；若宿主没注入钩子（例如把 WebUi 单独嵌进别的程序），这里会返回 501。

## 5. HTTP 接口

接口都在 `/api/` 下，JSON 一律 UTF-8；`GET` 返回对象或**裸数组**（注意不是包一层 `{"xxx":[...]}`）；出错统一 `{"error":"…"}`。下表是按源码逐条核对过的 22 条路由（11 条 GET + 11 条 POST），`HEAD` 复用对应 GET 的路由（只回响应头，不写正文）。

### 5.1 只读 GET

| 方法 路径 | 查询参数 | 返回 |
| --- | --- | --- |
| `GET /api/status` | — | 对象：`gameRoot` / `platform` / `chapterSuffix` / `mods` / `enabled` / `profiles[]` / `active_profile` / `lang` / `theme` / `version` / `port` / `from` |
| `GET /api/mods` | — | 裸数组：`[{id,name,author,version,enabled,chapter,dir}]` |
| `GET /api/profiles` | — | 裸数组：`[{id,name,description,enabled[],chapters[],active}]` |
| `GET /api/snapshots` | `modId`（可选） | 裸数组：`[{modId,version,name,author,created,source,files,bytes,note}]` |
| `GET /api/restore` | — | 裸数组：`[{id,name,created,from,profileId,chapters[],mods[],bytes,hasData,gameVersion}]` |
| `GET /api/gb/search` | `q`（必填）、`perPage`（默认 15，夹在 1..50） | `{ok,query,count,filtered,results[]}`；results 元素 `{id,name,model,author,profileUrl,description,downloadCount,likeCount,updated}` |
| `GET /api/queue` | — | 裸数组：`[{id,modId,modName,fileName,state,error,added,updated,size,got,url,localPath}]` |
| `GET /api/plugins` | — | 裸数组：`[{id,state,dir,diagnostics[],name,version,author,description,permissions[]}]` |
| `GET /api/themes` | — | `{ok,active,themes:[{id,name,author,description,builtIn,colors{},fontFamily,fontSize}]}` |
| `GET /api/lang` | — | `{ok,active,available[],builtIn[]}` |
| `GET /api/lang/coverage` | `code`（默认当前语言） | `{ok,code,result,report}`（`report` 是控制台文本） |

### 5.2 写操作 POST

| 方法 路径 | 请求体 | 返回 |
| --- | --- | --- |
| `POST /api/mods/toggle` | `{id 或 modId, enabled}` | `{ok,id,enabled}` |
| `POST /api/profile/use` | `{id}` | `{ok,id,changed}` |
| `POST /api/profile/save` | `{id, name?}` | `{ok,id,captured}` |
| `POST /api/snapshot/use` | `{modId, version, force?}` | `{ok,modId,version,restored}` |
| `POST /api/restore/apply` | `{id 或 pointId, force?}` | `{ok,id,applied}` |
| `POST /api/gb/install` | `{modId, type?, chapter?, fileId?, force?}` | `{ok,modId,code}` |
| `POST /api/queue/run` | `{autoInstall? 默认 true, deleteAfter? 默认 false}` | `{ok,processed}` |
| `POST /api/theme/use` | `{id}` | `{ok,id,code}` |
| `POST /api/lang/use` | `{code}` | `{ok,code}` |
| `POST /api/deploy` | `{chapter? 默认 root}` | `{ok,chapter,code}`；未注入钩子 → 501 |
| `POST /api/shutdown` | —（空体即可） | `{ok,message}` |

> 请求体统一是 JSON（`Content-Type: application/json`），上限 1 MB；字段名大小写敏感，`id` 与 `modId` 两种写法在对应接口里都认。

### 5.3 实测样例（原样粘贴，2026-09-26，端口 7936）

`GET /api/status`

```
{"gameRoot":"E:\\steam\\steamapps\\common\\DELTARUNE","platform":"windows","chapterSuffix":"windows","mods":67,"enabled":45,"profiles":["rpsA","rpsOff"],"active_profile":"smoke1","lang":"zh","theme":"dark","version":"1.0.0","port":7936,"from":"web"}
```

`GET /api/lang`

```
{"ok":true,"active":"zh","available":["zh","en","de","es","fr","ja","ko","ru","zh-tw"],"builtIn":["zh","en"]}
```

`GET /api/lang/coverage`

```
{"ok":true,"code":"zh","result":0,"report":"===== 语言覆盖率 =====\r\n  词条总数: 1854（当前语言 zh）\r\n  游戏内词条: 416 条（api/ 的 zh 表）\r\n[语言] zh  总键 1854  已译 1854  缺失 0  覆盖率 100.0%"}
```

`GET /api/gb/search?q=test&perPage=200`（`perPage` 被夹到上限 50；命中 14 条，截前两条）

```
{"ok":true,"query":"test","count":14,"filtered":0,"results":[{"id":698708,"name":"BTCYOADSG - Custom Chart","model":"Mod","author":"The14thBananaKing","profileUrl":"https://gamebanana.com/mods/698708","description":"","downloadCount":0,"likeCount":0,"updated":""},{"id":666188,"name":"Deltarune BUT You are Queen","model":"Mod","author":"JSHGHDDFSG","profileUrl":"https://gamebanana.com/mods/666188","description":"","downloadCount":0,"likeCount":2,"updated":""}]}
```

`GET /api/themes`（截第一个主题）

```
{"ok":true,"active":"dark","themes":[{"id":"dark","name":"深色（默认）","author":"Neutraled","description":"Neutraled 默认深色配色（黑底蓝调，久看不累）","builtIn":true,"colors":{"bg":"#101014","panel":"#16161e","fg":"#e6e6e6","dim":"#8a8a96","border":"#2a2a38","accent":"#4fc3f7","highlight":"#ffd54f","selection":"#2f5fd0","ok":"#7ee081","warn":"#ffb74d","error":"#ff6b6b"},"fontFamily":"","fontSize":0}]}
```

### 5.4 错误形状（实测）

| 情况 | 状态码 | 正文 |
| --- | --- | --- |
| 未知 API 路由 | 404 | `{"error":"未知路由: GET /api/nope"}` |
| 静态文件不存在 | 404 | `{"error":"找不到资源: /nope.txt"}` |
| 搜索缺 `q` | 400 | `{"error":"缺少查询词 q"}` |
| id 含 `..` 或分隔符 | 400 | `{"error":"mod id 非法: ../evil"}`、`{"error":"mod id 非法: ../etc"}` |
| 缺必填字段 | 400 | `{"error":"缺少 enabled 参数"}`、`{"error":"配置档 id 非法: "}` |
| 请求体超过 1 MB | 400 | `{"error":"请求体超过 1 MB 上限"}`（源码行为，未实测） |
| 请求体不是 JSON | 400 | `{"error":"请求体不是合法 JSON: 'b' is an invalid start of a property name. Expected a '\"'. LineNumber: 0 \| BytePositionInLine: 1."}` |
| 非本机访问 | 403 | `{"error":"只允许本机访问"}`（源码行为；本机只绑回环，未能在真机上复现） |
| 部署钩子未注入 | 501 | `{"error":"部署钩子未注入：请由主程序设置 WebUi.DeployHook"}`（源码行为；CLI 已注入，所以命令行下不会出现） |
| 处理异常 | 500 | `{"error":"处理 <路径> 失败: <异常消息>"}` |

## 6. 静态资源

- 查找顺序：`<exe 所在目录>/web`（发布包布局）→ `Neutraled/web`（仓库布局），两处都要有 `index.html` 才算命中；
- 都找不到时返回内置兜底页：「未找到 web/index.html：网页界面资源缺失，接口仍在运行。请确认发布包里包含 web/ 目录（index.html / app.js / style.css），或把它们放进 Neutraled/web/ 下。」——**接口照常可用**，只是没有面板；
- MIME：`.html/.htm`、`.js/.mjs`、`.css`、`.json`、`.svg`、`.png/.jpg/.jpeg/.gif/.ico`、`.woff2/.woff`、`.txt/.md` 都有对应类型，其它走 `application/octet-stream`；
- 所有静态响应都带 `Cache-Control: no-store`：改完 `app.js` 刷新页面即生效，不用清缓存。

实测：`GET /` → 200 `text/html; charset=utf-8`（1101 字节，就是 `index.html`）；`GET /style.css` → 200 `text/css; charset=utf-8`（6690 字节）。

## 7. 和命令行的关系

- **同一条代码路径**：WebUi 自己零业务逻辑，只把请求转给模块公开 API（`Profiles` / `Snapshots` / `RestorePoints` / `GbBrowse` / `DownloadQueue` / `Plugins` / `Themes` / `LangPacks`），和 `--profile-use`、`--snapshot-use` 这些开关做的是同一件事。网页里改的东西命令行立刻看得到，反之亦然。
- 网页独有的便利：一次开关多个 mod、色板预览、覆盖率文本、状态每 15 秒自动刷新。
- 只有命令行有的：`--adapt`、`--pack`、`--plugin-install/--plugin-remove`、`--make-cjk-font`、`--cache-*`、`--i18n-*` 等一大票开关，以及所有需要 `--force` 的批量操作。
- `POST /api/deploy` 走主程序注入的 `DeployHook`（CLI 里就是 `DeployAll`），所以网页部署和 `--deploy` 是同一套逻辑。
- 路由清单与状态由 `WebUi.PrintStatus()` 输出，但目前没有 CLI 开关调用它（见下）。

## 已知限制

- **没有鉴权**：只靠「只绑回环 + 每请求验来源」。同一台机器上的任何程序都能调这些接口；没有 token、没有 HTTPS、没有 CSRF token、没有 CORS 头。
- **不是远程 / 多用户方案**：不监听 `0.0.0.0`，也没有配置项能改绑定地址（要改只能改源码重新构建）。
- **`--port` 只是起始端口**：被占用就 +1，最多试 6 个；实际端口以打印的 URL 或 `/api/status` 的 `port` 字段为准。
- **`--auto-stop` 是空闲看门狗**：默认 120 秒，`NTL_WEB_IDLE_MS` 可覆盖（200..86400000 毫秒）。它**不会**因为「浏览器关了」而退出，只按有没有 HTTP 请求判断。
- **没有 `--web-status`**：`WebUi.PrintStatus()` 有完整的状态 / 路由输出，但没有任何 CLI 开关接到它（`--help` 里也查不到）。想看状态就用网页总览或 `GET /api/status`。
- **`GET /api/lang/coverage` 的 `report` 是控制台文本**：服务端临时接管 `Console.SetOut` 来抓取覆盖率打印，所以它是给人看的字符串（含 `\r\n`），不是结构化字段。
- **章节下拉只到 `chapter4`**：`app.js` 里的章节表是 `['root','chapter1'..'chapter4']`，比命令行能表达的章节范围窄；更新的章节要用命令行，或直接给接口传 `chapter`。
- **插件面板只读**：安装 / 卸载 / 启用插件仍然只能用命令行。
- **破坏性操作没有服务端二次确认**：`window.confirm` 是前端行为，直接 POST 就会执行。
- **未实测**（源码行为，未在本机复现）：非本机访问的 403、未注入钩子时的 501、macOS / Linux 的 `open` / `xdg-open` 分支、Ctrl+C 退出、`--port` 的取值边界、`HEAD` 请求。
- 本机只在 **Windows** 上验证过（默认 7931 与实测用的 7933 / 7935 / 7936 端口）。界面是零依赖原生 JS，理论上跨平台，但没有在其它平台上跑过。
- 本文档的实测输出取自 2026-09-26 的 `builder/bin/Release/net9.0/ntl-builder.exe`（该二进制在文档写作期间被 Captain 反复重建）。行为以 `builder/WebUi.cs` 与 `web/app.js` 的当前源码为准。
