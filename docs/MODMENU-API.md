# Mod 设置面板 · 开发者接口

Neutraled 在游戏自己的「设置」菜单（CONFIG）里加了一行 **Mod 设置**，位置与样式和官方项完全一致 ——
它是 **第 6 行**（`y = yy + 325`），原版「Return to Title」「Back」被顺移到第 7、8 行（`yy + 360` / `yy + 395`）。
光标移过去按 `Z` / `Enter` 打开。

打开后画的是**游戏自己的二级菜单**（原生 submenu 页，`global.submenu = 51`）：
黑框 + 星角边框、行高 35、选中红心 sprite（**按名字解析** `spr_heart`：chapter4=`3695` / chapter1=`922`，
实现见 `api/ntl_heart_sprite.gml`；整包 mod 的索引不可移植 ⇒ lint 规则 17 禁止裸写索引）、右侧细滚动条、底部说明行 —— 与官方二级菜单同一套画法
（字体用官方菜单的 `mainbig`，见 [MANAGE.md](MANAGE.md) §11.4）。
主列表长这样（标题 + 5 行可见 + 底部说明行）：

```
MOD 设置
  章节选择                        7 个章节
  已加载模组                      3 个模组
  界面语言                    < zh >
  重新部署并重启
  ← 你的 mod 项出现在这里 →
  关闭面板                 ← 超过 5 行自动滚动（右侧三角提示）

 说明行：随光标显示当前这一行的说明（同时也是消息行）
```

**任何 mod 都可以往这个列表里加自己的项**，不需要改 Neutraled 本体。

## 1. 加一项（3 行代码）

在 mod 自己的脚本里调用。脚本放在 **`mods/<mod 名>/<作者>/<章节>/gml/*.gml`**（注意是 `gml/`，不是 `scripts/`；
`scripts/` 是 lua 用的），**文件名就是脚本名**，其中名为 **`main.gml` 的那个是入口**，部署时被注入进
`scr_ntl_init` 的清单、在游戏启动时执行一次（其余文件只是同名脚本资源，等别人调用）。
入口内直接写顶层语句即可（老式脚本写法，不需要包成函数）：

```gml
ntl_menu_add("mymod.speed", "速度倍率", "2x", "mymod_toggle_speed", "按键切换游戏速度倍率");
```

- 第 1 个参数：**唯一 id**（重复调用 = 覆盖，热重载不会出现两行）
- 第 2 个：左侧标签文案
- 第 3 个：右侧数值文案（可留空 `""`）
- 第 4 个：**动作脚本名**（玩家在这一项上按 `Z`/`Enter` 时调用）；留空 = 只显示、按了没反应
- 第 5 个（可选）：这一项的**说明**，画在面板底部说明行（官方二级菜单那个位置）

第 4 个参数是**脚本名（字符串）**，Neutraled 用 `asset_get_index(名字)` 去找 ⇒ 它必须是**真实存在的脚本资源**，
所以每个动作都得是 `gml/` 目录下的**独立 .gml 文件**（文件名 = 动作名）。不能传函数引用、也不能传 lambda。
动作脚本被调用时**不带参数**。动作脚本就是普通脚本：

```gml
/// mymod_toggle_speed()
global.mymod_speed = (global.mymod_speed == 1) ? 2 : 1;
ntl_menu_set_value("mymod.speed", string(global.mymod_speed) + "x");
```

## 2. 多语言文案

传 `"i18n:键名"` 就走 i18n 表（键放在你 mod 自己的 `files/lang/*.json` 里），否则按字面量画。
标签、数值、说明三个字段都支持：

```gml
ntl_menu_add("mymod.speed", "i18n:mymod.speed", "i18n:mymod.speed.value", "mymod_toggle_speed", "i18n:mymod.speed.desc");
```

## 3. 完整接口

| 函数 | 作用 |
| --- | --- |
| `ntl_menu_add(id, label, value, action, desc)` | 加一项 / 覆盖同 id 项（`desc` 可选，画在说明行） |
| `ntl_menu_set_value(id, value)` | 只改右侧数值文案（返回 1 = 改到了） |
| `ntl_menu_remove(id)` | 撤掉自己加的一项 |
| `ntl_menu_clear()` | 清空所有 mod 注册项（Neutraled 重载 mod 时自己调） |
| `ntl_menu_count()` | 当前注册了几项（只读） |
| `ntl_menu_entry(i)` | 取第 i 项的 ds_map：`id` / `label` / `value` / `action` / `desc`（只读，越界返回 -1） |

## 4. 游戏本身那一行也想改？

入口那一行的**标签与数值**由 Neutraled 自己画（`api/ntl_settings_row_draw.gml`，3 个参数，
注入 `obj_darkcontroller` 的 Draw、插在官方红心那一行之前），文案取 i18n 键 `set.row`（标签）
与 `set.value`（数值，参数 1 = **本产物实际加载**的 mod 数，读产物目录的 `Neutraled/mods.json`）。

**红心不用它画** —— 游戏自己的 `draw_sprite(<红心 sprite 索引>, 0, _heartXPos, yy + 160 + coord * 35)` 正好落在第 6 行上
（那个索引每章不同：chapter4=3695、chapter1=922，所以 Neutraled 自己的面板一律用 `ntl_heart_sprite()` 按名字取，见 [MANAGE.md §11.4](MANAGE.md) 教训 5），
坐标公式原样复用（这也是「和官方一致」的关键：行高 35、缩进 `_xPos`、数值列 `_selectXPos` 都用官方那三个变量）。
纯 ASCII 文案沿用游戏当前字体（`fnt_main`），只有中文/日文才切 `ntl_font_cjk`。
部署时会对 `fnt_main` 等字体做**缺字补全**（见 [MANAGE.md §11.2](MANAGE.md)），所以 mod 用自己的语言写文案也不会画不出来。

按键同样在 `obj_darkcontroller` 的 Step 里接管（`api/ntl_settings_row_press.gml`），
注入点在原版 `if (global.submenucoord[30] == 0)` 之前、且已在 `button1_p() && onebuffer < 0` 分支内
（那时游戏的按键缓冲 `onebuffer`/`twobuffer` 刚被置位）⇒「一次按键只触发一次」由游戏自己的缓冲保证，
面板页也不会把打开面板的那一次按键再吃一遍。

## 5. 实战案例：60 FPS mod 的 6 个开关

`mods/60fps_layer/badartadventure/<章>/` 是**源码级差异层**（整包 mod 抽出来的补丁），它原本自带一个
`MOD SETTINGS` 页（`submenu == 50`）—— 但那一页所在的 `obj_darkcontroller` 的 Draw/Step 已经交还给
Neutraled 自己注入（见 [PIPELINE.md](PIPELINE.md)「层不许覆盖 Neutraled 自己补丁的对象」），所以该页进不去。
改造方式就是本文档的接口：

| 文件（`gml/`，文件名 = 脚本名） | 作用 |
| --- | --- |
| `main.gml` | 入口，只调 `fps_ntl_menu_register();` |
| `fps_ntl_menu_register.gml` | 6 次 `ntl_menu_add(...)` + `ntl_menu_remove(...)`（先清自己的旧 id，热重载不重复） |
| `fps_ntl_menu_value.gml` | 把开关状态翻成文案（关 / 渐进 / 完全 / 2 倍 / 4 倍 …） |
| `fps_ntl_menu_desc.gml` | 6 条中文说明 |
| `fps_ntl_flag.gml` / `fps_ntl_set_flag.gml` | 安全读写 `global.flag[n]`（存档字段可能不存在/是字符串） |
| `fps_ntl_toggle_{ow,bt,debug,gerson,speed,textskip}.gml` | 6 个动作脚本，各自改状态后 `ntl_menu_set_value(id, ...)` 刷新数值 |

注册的 6 个 id：`fps.ow_stick` / `fps.bt_stick` / `fps.debug` / `fps.gerson` / `fps.speed` / `fps.textskip`。
面板里它们排在 4 个固定项之后（主列表变成 4 + 6 = 10 行，超过 5 行自动滚动）。

两条经验：

1. **数值要自己刷新**：`ntl_menu_set_value` 只改文案，Neutraled 不会替你去读状态 ⇒ 动作脚本里读完写完顺手刷新一次。
2. **文案进字体字符集**：部署时字体补全会扫 `mods/**/gml/*.gml` 里的字符串字面量（见 [MANAGE.md §11.2](MANAGE.md)），
   所以中文标签/说明能画出来；反过来说**别把文案拼在运行时**（例如 `"速度" + string(n)`），那样补全扫不到，画出来是空洞。

## 6. 排错

- 日志：`%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`
  - `[接口] 面板项已注册: <id>` = 注册成功
  - `[接口] 触发 <脚本名>` = 动作被调用
  - `[接口] 找不到脚本 <名>（id=…）` = 脚本名写错或脚本没被加载
  - `[面板] 已打开（submenu 51，已加载 N / 已安装 M 个模组）` / `[面板] 已关闭` = 进出面板
- 面板里 mod 项排在「重新部署并重启」下面、「关闭面板」上面；主列表按 `X` 回设置菜单，子列表按 `X` 回主列表。
- 退出码与「面板不出现」：先看部署日志里那几条 `增强:` 有没有全（缺哪条就说明那个锚点没命中），
  再确认玩的是**部署过的那一章**。
