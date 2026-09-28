Neutraled {{VERSION}} —— DELTARUNE 的 Everest 式 mod 管理器 / API
============================================================

本发布包只有两个目录：

  install/   安装器（单文件自包含 exe，不需要另装 .NET）
  src/       源码 + 运行期载荷（api/ docs/ lang/ fonts/ tools/ … 以及 builder/ gui/ studio/ 源码）

安装
----
把 install/ 与 src/ 保持同级放在一起，然后运行：

  install\ntl-builder.exe --install                          自动检测 Steam 游戏目录
  install\ntl-builder.exe --install --game "D:\...\DELTARUNE"    手动指定游戏目录

安装器会把 src/ 的内容装进 <游戏>\Neutraled\，并把安装器自己复制到 <游戏>\Neutraled\bin\。

装完之后（都在 <游戏>\Neutraled\bin\ 下）：

  ntl-builder.exe --deploy-all     部署全部章节并启动游戏
  ntl-builder.exe --web            打开网页界面（浏览器里选章节 / 管理 mod）
  ntl-builder.exe --help           全部命令一览

卸载 / 还原
----------
  <游戏>\Neutraled\bin\ntl-builder.exe --uninstall

说明
----
* install\ntl-builder.exe 是自包含单文件（内含 .NET 运行时）；旁边的 Magick.Native-Q8-x64.dll
  是图像处理用的原生库，必须和 exe 放在同一目录。
* 想从源码自己编译（需要 .NET 9 SDK）：还要有 UTMT 依赖库（UndertaleModLib.dll / Underanalyzer.dll /
  Newtonsoft.Json.dll / Magick.NET-Q8-AnyCPU.dll）。把 UTMT 目录放到 <游戏根>\UTMT\，或设置环境变量
  NTL_UTMT_DIR 指向它，然后在 src\builder\ 下执行 dotnet build -c Release；缺依赖时构建会明确报
  「找不到 UTMT 依赖库」并提示这两种办法。
* 文档从 src\docs\GETTING-STARTED.md 起步，docs\ 下共 12 篇。

Neutraled {{VERSION}} -- an Everest-style mod manager / API for DELTARUNE
  install/  the installer (single self-contained exe, no .NET runtime needed)
  src/      sources + runtime payload
  Run : install\ntl-builder.exe --install [--game "<game dir>"]
  Then: <game>\Neutraled\bin\ntl-builder.exe --deploy-all | --web | --help