Option Explicit
Dim sh, fs, folder, engine, installed, manifest, cmd
Set sh = CreateObject("WScript.Shell")
Set fs = CreateObject("Scripting.FileSystemObject")
folder = fs.GetParentFolderName(WScript.ScriptFullName)
engine = fs.BuildPath(folder, "UpdatePromptDesk.ps1")
If Not fs.FileExists(engine) Then
  installed = ""
  On Error Resume Next
  installed = sh.RegRead("HKCU\Software\PromptDesk\InstallDir")
  On Error GoTo 0
  If installed = "" Then installed = sh.ExpandEnvironmentStrings("%LOCALAPPDATA%\Programs\PromptDesk")
  engine = fs.BuildPath(installed, "UpdatePromptDesk.ps1")
End If
If Not fs.FileExists(engine) Then
  MsgBox "Primero aplica la transicion de parches de PromptDesk.", 48, "PromptDesk"
  WScript.Quit 1
End If
cmd = "powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Chr(34) & engine & Chr(34)
manifest = fs.BuildPath(folder, "patch.json")
If fs.FileExists(manifest) Then cmd = cmd & " -PatchFile " & Chr(34) & manifest & Chr(34)
sh.Run cmd, 0, True
