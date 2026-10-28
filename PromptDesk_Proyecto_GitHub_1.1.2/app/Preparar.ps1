$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'No se encontró .NET Framework 4.x. Este programa necesita Windows 10/11 con .NET Framework habilitado.' }
    $arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/out:PromptDesk.exe','/win32icon:assets\PromptDesk.ico','/resource:assets\PromptDesk.ico,PromptDesk.ico','/resource:assets\PromptDesk.png,PromptDesk.png','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.Xml.Linq.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','src\Core.cs','src\App.cs','src\Tests.cs','src\History.cs','src\Theme.cs','src\Branding.cs','src\Installation.cs','src\UpdateCenter.cs')
    $log = & $compiler @arguments 2>&1
    $compilerExit = $LASTEXITCODE
    $log | Out-File -LiteralPath 'compilacion.log' -Encoding utf8
    if ($compilerExit -ne 0) { throw 'Falló la compilación. Ver compilacion.log.' }
    $test = Start-Process -FilePath (Join-Path $PSScriptRoot 'PromptDesk.exe') -ArgumentList '--test' -Wait -PassThru
    if ($test.ExitCode -ne 0) { Remove-Item -LiteralPath 'PromptDesk.exe'; throw 'Fallaron las pruebas de integridad. Ver pruebas-error.txt.' }
    exit 0
} catch {
    $_ | Out-File -LiteralPath 'compilacion.log' -Append -Encoding utf8
    exit 1
}
