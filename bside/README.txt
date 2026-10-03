Neutraled —— B 面（B-side）存档模板目录
Neutraled -- B-side save template directory

这里有两份来源不同的模板，**物理上分开放**，为的是让个人存档不可能混进发布包：
  · bside\templates\chapterN.sav  —— **随包默认模板**（已抹掉个人数据，随发布包分发，装完就能用）
  · bside\chapterN.sav            —— **你自己导入的模板**（--import-bside 写在这里），属于个人数据，
                                      **不会**进发布包：tools\package.ps1 与发布工程只保留 templates\ 里的。
--make-bside 的取用顺序：先用你自己导入的 chapterN.sav，没有才用 templates\chapterN.sav；
两份都没有时才走下面的兜底（把现有存档标记为 B 面）。

用法（在游戏根目录下执行，N = 章节号，S = 槽位 0/1/2）：
  1) 导入一份真实的 B 面存档（**默认直接落到槽位并标记 B 面**）：
       Neutraled\bin\ntl-builder.exe --import-bside "<你的B面存档>" [--chapter chapterN] [--slot S] [--save-name <名字>] [--all-slots] [--template-only]
     · 章节/槽位不给就从**文件名**识别（filech2 → 第 2 章槽位 0；filech3_1 → 第 3 章槽位 1）；
       识别不出来要显式给 --chapter。
     · 导入的模板一律按**实际章节**存成 bside\chapterN.sav（源文件叫什么不重要）。
     · --save-name 是**角色名**：游戏里存档文件**第 1 行就是角色名**，玩家命名上限 12 个字母
       （自动转大写，与游戏内命名界面一致）。不填时会在终端里提示输入；非交互环境必须显式给。
     · 默认会把内容写进 Neutraled\saves\DELTARUNE\filechN_S，并写 dr.ini 的 SideB=1 / Name。
       加 --template-only 就只建模板，不碰游戏存档。
  2) 用模板生成/标记该章节槽位的 B 面存档（日志第一行会说明用的是哪份模板）：
       Neutraled\bin\ntl-builder.exe --make-bside --chapter N --slot S
       Neutraled\bin\ntl-builder.exe --make-bside --chapter N --all-slots

没有模板时的兜底：如果该章节槽位**已经有存档**（正常进过一次），
`--make-bside` 会直接把那份存档标记为 B 面（内容与标记一致，不是凭空伪造）。
两份模板都没有、槽位也空着时会报错，并给出上面两条路的提示。

图形界面：Neutraled 窗口 → 工具箱 → 「导入 B 面存档」（选文件、填名字、勾选是否直接写入）。

存档位置：Neutraled\saves\DELTARUNE\filechN_S（纯文本），B 面标记写在同目录 dr.ini 的
[G_N_S]（N>=2）或 [GS]（N=1）段的 SideB 键；角色名同时写在存档文件第 1 行与 dr.ini 的 Name。

重要：游戏正在运行时不要导入 —— 退出游戏时它会把自己的存档写回去，覆盖导入的结果。

---
B-side save templates. Two kinds, kept in separate places on purpose, so that personal
saves cannot leak into a release package by accident:
  * bside\templates\chapterN.sav -- the SHIPPED DEFAULT template (personal data scrubbed;
    part of the release package, works right after install).
  * bside\chapterN.sav           -- the template YOU imported (--import-bside writes here).
    It is personal data and never enters a release package: tools\package.ps1 and the
    release tooling keep only what is under templates\.
--make-bside prefers your own chapterN.sav, falls back to templates\chapterN.sav, and only
when neither exists does it convert an existing save (see below).

Import (applies to a slot and marks the B-side by default):
  ntl-builder.exe --import-bside "<real B-side save>" [--chapter chapterN] [--slot S] [--save-name <NAME>] [--all-slots] [--template-only]
  - chapter/slot are detected from the file name (filech2 -> chapter 2, slot 0);
    pass --chapter when the name does not say it.
  - an imported template is always stored under the ACTUAL chapter: bside\chapterN.sav.
  - --save-name is the character name: line 1 of a save file IS the character name
    (the in-game naming screen allows up to 12 letters; it is uppercased).
  - without --template-only the content is written to Neutraled\saves\DELTARUNE\filechN_S
    and dr.ini gets SideB=1 / Name.
Apply : ntl-builder.exe --make-bside --chapter N --slot S [--all-slots]   (the log says which template was used)
GUI   : Neutraled window -> Toolbox -> "Import B-side Save".

Fallback with no template: if that chapter/slot already has a save (you entered the chapter
normally once), --make-bside just marks that save as B-side (the mark matches the content;
nothing is fabricated). With neither a template nor a save it errors out and lists both routes.

Do not import while the game is running: on exit the game writes its own saves back and
overwrites the imported one.
