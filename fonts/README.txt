Neutraled\fonts\ —— 逐文件来源与许可速查
==============================================================
（每个字形的来源都在本文件与 OFL-NOTICE.txt 里说明。仓库/发布包内**没有任何游戏字体位图
  或汉化字库位图**；游戏字体只在用户本机部署时临时生成到 <Neutraled>\.tmp\font-native\。）

文件                              内容                                        字形来源 / 许可
--------------------------------------------------------------
ntl_font_cjk.json                 字形表 4974 字形 / EmSize=12 /            全部字形位图 = OFL Noto 家族
                                  LineHeight=18 / Ascender=11 / 多 sheet      离屏渲染（OFL-1.1）
ntl_font_cjk_o1.png   4096x512    4689 字形（主表：拉丁/标点/汉字/全角）      Noto Sans SC
                                                                             + ofl\OFL-notosanssc.txt
ntl_font_cjk_o2.png   64x32      3 字形（符号/emoji 兜底）                  Noto Sans Symbols 2 / Noto Emoji
                                                                             + ofl\OFL-notosanssymbols2.txt、OFL-notoemoji.txt
ntl_font_cjk_o3.png   4096x32    232 字形（日/韩兜底）                      Noto Sans JP / KR
                                                                             + ofl\OFL-notosansjp.txt、OFL-notosanskr.txt
ntl_font_cjk_o4.png   1024x32    50 字形（日/韩兜底续）                     同上
ntl_native_sources.json           部署期字形来源声明（码位区间 + 来源种类，    不产生字形；许可与合规说明见
                                  **不含任何位图**）                          OFL-NOTICE.txt 第七节
OFL-NOTICE.txt                    来源与许可声明（逐页来源/上游地址/          OFL-1.1 合规文档（随包分发）
                                  SHA-256/合规做法 + 本机覆盖说明）
ofl\OFL-noto*.txt（5 份）          SIL Open Font License 1.1 全文              SIL OFL 1.1（随包分发）

一句话：随包字形 = OFL Noto（可再分发、已附许可全文）；游戏原生字形（8bitoperator JVE 与汉化像素汉字）
只在**部署期**由用户本机的 data.win 生成到 .tmp\，从不进仓库/发布包。
