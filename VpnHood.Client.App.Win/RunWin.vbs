Set oShell = CreateObject("WScript.Shell")
Set oFso = CreateObject("Scripting.FileSystemObject")
Set oShellApp =  CreateObject("Shell.Application")
curDir = oFso.GetParentFolderName(Wscript.ScriptFullName)

' *** install
launcher = "dotnet"
action = "runas"
file = chr(34) & oFso.BuildPath(curDir, "run.exe") & chr(34)
msgbox file + " /nowait"
oShellApp.ShellExecute "dotnet.exe", file + " /nowait", "", action, 0