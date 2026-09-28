Neutraled —— B 面（B-side）存档模板目录
Neutraled -- B-side save template directory

这个目录放**你自己**的 B 面存档模板，发布包里是空的（模板属于玩家个人数据，不随包分发）。
本目录里的 *.sav 不会被 tools\package.ps1 打进发布包。

用法（在游戏根目录下执行，N = 章节号，S = 槽位 0/1/2）：
  1) 导入一份真实的 B 面存档作为模板：
       Neutraled\bin\ntl-builder.exe --import-bside "<你的B面存档>" --chapter N
     导入后本目录会出现 chapterN.sav。
  2) 用它生成/标记该章节槽位的 B 面存档：
       Neutraled\bin\ntl-builder.exe --make-bside --chapter N --slot S
       Neutraled\bin\ntl-builder.exe --make-bside --chapter N --all-slots

没有模板时的兜底：如果该章节槽位**已经有存档**（正常进过一次），
`--make-bside` 会直接把那份存档标记为 B 面（内容与标记一致，不是凭空伪造）。
两者都没有时会报错并给出上面两条路的提示。

存档位置：Neutraled\saves\DELTARUNE\filechN_S（纯文本），B 面标记写在同目录 dr.ini 的
[G_N_S]（N>=2）或 [GS]（N=1）段的 SideB 键。

---
Put YOUR OWN B-side save template here (chapterN.sav). The release package ships this
directory empty: templates are personal data. Files matching *.sav here are excluded
from the release package by tools\package.ps1.

Import: Neutraled\bin\ntl-builder.exe --import-bside "<real B-side save>" --chapter N
Apply : Neutraled\bin\ntl-builder.exe --make-bside --chapter N --slot S [--all-slots]
