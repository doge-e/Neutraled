' Neutraled watcher launcher - starts --watch-external fully hidden (no window, no dialog).
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
ntl = "E:\steam\steamapps\common\DELTARUNE\Neutraled\builder\bin\Release\net9.0\ntl-builder.exe"
If Not fso.FileExists(ntl) Then WScript.Quit 0
root = "E:\steam\steamapps\common\DELTARUNE\Neutraled"
sh.CurrentDirectory = root
sh.Run """" & ntl & """ --watch-external", 0, False
