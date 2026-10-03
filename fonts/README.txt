Neutraled\fonts\ —— 逐文件来源与许可速查
==============================================================
（每个字形的来源都在本文件与 OFL-NOTICE.txt 里说明。仓库/发布包内**没有任何游戏字体位图
  或汉化字库位图**；游戏字体只在用户本机部署时临时生成到 <Neutraled>\.tmp\font-native\。）

文件                              内容                                        字形来源 / 许可
--------------------------------------------------------------
ntl_font_cjk.json                 字形表 4974 字形 / EmSize=12 /            953 个非中文字形 = Ark Pixel 12px
                                  LineHeight=18 / Ascender=11 / 多 sheet      （OFL-1.1）；其余 4021 个 = OFL Noto 家族
ntl_font_cjk_ark12.png 4096x2048  953 字形（ASCII/拉丁-1/拉丁扩展/希腊/       Ark Pixel 12px（12px monospaced 简体子集）
                                  西里尔/标点/符号 —— 2026-10-02 换像素风）   + ofl\OFL-arkpixel.txt
ntl_font_cjk_o1.png   4096x512    3978 字形（主表：拉丁/标点/汉字/全角）      Noto Sans SC
                                                                             + ofl\OFL-notosanssc.txt
ntl_font_cjk_o2.png   64x32      3 字形（符号/emoji 兜底）                  Noto Sans Symbols 2 / Noto Emoji
                                                                             + ofl\OFL-notosanssymbols2.txt、OFL-notoemoji.txt
ntl_font_cjk_o3.png   4096x32    23 字形（日/韩兜底）                       Noto Sans JP / KR
                                                                             + ofl\OFL-notosansjp.txt、ofl\OFL-notosanskr.txt
ntl_font_cjk_o4.png   1024x32    17 字形（日/韩兜底续）                     同上
ntl_native_sources.json           部署期字形来源声明（码位区间 + 来源种类，    不产生字形；许可与合规说明见
                                  **不含任何位图**）                          OFL-NOTICE.txt 第七节
OFL-NOTICE.txt                    来源与许可声明（逐页来源/上游地址/          OFL-1.1 合规文档（随包分发）
                                  SHA-256/合规做法 + 本机覆盖说明）
ofl\OFL-*.txt（6 份）              SIL Open Font License 1.1 全文              SIL OFL 1.1（随包分发；5 份 Noto
                                                                             + 1 份 Ark Pixel）

一句话：随包字形 = OFL Noto + OFL Ark Pixel（可再分发、已附许可全文；非中文码位用 Ark Pixel 像素字，
与游戏像素风一致）；游戏原生字形（8bitoperator JVE 与汉化像素汉字）只在**部署期**由用户本机的
data.win 生成到 .tmp\，从不进仓库/发布包。

字形垂直对齐（2026-10-02，改字体包前必读）
--------------------------------------------------------------
GameMaker 画字的位置 = 「行顶 + 裁剪块内的墨迹行」（模型 M1，真机像素实测：块的顶线全字体共享，
SourceHeight / Offset 都不产生垂直位移）。游戏自带 fnt_main 的拉丁字形墨迹底在第 12 行，
所以本包里的字形也必须落在同一行：

  非降部字形（A a H Ä ä ß é ñ 中）  墨迹底 = 第 12 行
  降部字形（g y p ç j）            墨迹底 = 第 14 行

踩过的坑：旧包用 ImageMagick label: 渲染，墨迹底在第 9 行 ⇒ 带声调字母（ä ü ß é ñ ç）在屏幕上
比同行 ASCII 高 3 px（用户反馈「修复的字向上偏移」）。2026-10-02 已把 953 个 Ark 字形
整体下移 3 行（ark12 页从 691,283 B 变 44,872 B）。

改包后的复核（两条都要过）：
  1) 产物级：ntl-builder.exe --font-probe <章节 data.win> <输出目录>  ⇒ ä/ü/ß 墨迹底 = 12、ç = 14、A = 12
  2) 真机级：控制台 log 一行「MIX Aa ÄÖÜäöüßéñçÇ 中文 HHH」，截图逐字量墨迹底（ASCII 与非降部带声调字必须相等）
修复/复核脚本：tools/font-align-fix.py（--apply 应用；不带参数只打印直方图）
