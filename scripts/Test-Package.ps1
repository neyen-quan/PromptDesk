param([Parameter(Mandatory=$true)][string]$OldRoot,
      [Parameter(Mandatory=$true)][string]$NewRoot,
      [Parameter(Mandatory=$true)][string]$Patch)
$ErrorActionPreference='Stop'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('PromptDesk-package-'+[Guid]::NewGuid().ToString('N'))
try {
    $installed=Join-Path $temp 'installed'
    New-Item $temp -ItemType Directory|Out-Null
    Copy-Item -LiteralPath $OldRoot -Destination $installed -Recurse
    # Exercise the updater already installed in the exact published baseline.
    . (Join-Path $OldRoot 'UpdatePromptDesk.ps1') -LibraryOnly
    $personal=Join-Path $installed 'datos.xml'
    [IO.File]::WriteAllText($personal, '<synthetic-personal-data>preserve me</synthetic-personal-data>')
    $before=Hash-File $personal
    $manifest=Expand-PatchZip $Patch (Join-Path $temp 'expanded')
    Apply-Patch $manifest $installed | Write-Host
    if ((Hash-File $personal) -ne $before) { throw 'El parche modificó el archivo personal de prueba.' }
    foreach ($file in Get-ChildItem -LiteralPath $NewRoot -File) {
        if ((Hash-File (Join-Path $installed $file.Name)) -ne (Hash-File $file.FullName)) {
            throw ('El runtime actualizado difiere del paquete completo: '+$file.Name)
        }
    }
    $test=Start-Process -FilePath (Join-Path $installed 'PromptDesk.exe') -ArgumentList '--test' -WorkingDirectory $installed -PassThru
    if (!$test.WaitForExit(60000)) { $test.Kill(); throw 'El ejecutable actualizado no terminó sus pruebas.' }
    if ($test.ExitCode -ne 0) { throw 'El ejecutable actualizado falló sus pruebas.' }
    Write-Host 'PASS: exact published baseline -> installed updater -> new runtime; personal data preserved.'
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
