Option Explicit
Dim shell, fso, folder, exe
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
exe = fso.BuildPath(folder, "PromptDesk.exe")
shell.CurrentDirectory = folder
shell.Run Chr(34) & exe & Chr(34) & " --install", 1, False
