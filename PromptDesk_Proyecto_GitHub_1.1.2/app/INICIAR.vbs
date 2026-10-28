Option Explicit
Dim shell, fso, base, command, result
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
shell.CurrentDirectory = base
If Not fso.FileExists(base & "\PromptDesk.exe") Then
  command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Chr(34) & base & "\Preparar.ps1" & Chr(34)
  result = shell.Run(command, 0, True)
  If result <> 0 Then
    MsgBox "No se pudo preparar PromptDesk. Revisa compilacion.log en esta carpeta. Tambien podes usar INICIAR_ALTERNATIVO.cmd.", 16, "PromptDesk"
    WScript.Quit result
  End If
End If
shell.Run Chr(34) & base & "\PromptDesk.exe" & Chr(34), 1, False
