Set oShell = CreateObject("WScript.Shell")
Set oFso = CreateObject("Scripting.FileSystemObject")
Set oShellApp =  CreateObject("Shell.Application")
curDir = oFso.GetParentFolderName(Wscript.ScriptFullName)

' *** install
launcher = "dotnet"
action = "runas"
file =  oFso.BuildPath(curDir, "launcher\run.dll") 
fileDQ = chr(34) & file & chr(34)

If oFso.FileExists(file) Then
    oShellApp.ShellExecute "dotnet.exe", fileDQ & " /nowait", "", action, 0
else
    msgbox "Sorry! File does not exist: " & file, 16, "Error"
End If

