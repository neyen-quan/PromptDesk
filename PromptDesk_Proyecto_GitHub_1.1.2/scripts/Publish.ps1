$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$repo=$env:GITHUB_REPOSITORY
if(!$repo){throw 'Este script publica desde GitHub Actions.'}
$version=[regex]::Match((Get-Content app/src/Branding.cs -Raw),'const string Version="([^"]+)"').Groups[1].Value
if(!$version){throw 'Versión ausente'}
$tag='v'+$version
# Never replace a published release. A new executable requires a new version.
$releaseJson=& gh release list --repo $repo --limit 100 --json tagName,isDraft,isPrerelease
if($LASTEXITCODE -ne 0){throw 'No se pudieron consultar las releases existentes.'}
$releaseList=@($releaseJson|ConvertFrom-Json|Where-Object { !$_.isDraft -and !$_.isPrerelease })
if(@($releaseList|Where-Object { $_.tagName -eq $tag }).Count -gt 0){Write-Host "$tag ya está publicada. Incrementá Branding.Version para publicar cambios.";exit 0}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File app/Preparar.ps1
if($LASTEXITCODE -ne 0){throw 'Falló la compilación o las pruebas. Revisar app/compilacion.log.'}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Updates.ps1
if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas del actualizador.'}
$out=Join-Path $root 'dist';New-Item $out -ItemType Directory -Force|Out-Null
$runtime=Join-Path $out 'full/PromptDesk';New-Item $runtime -ItemType Directory -Force|Out-Null
$names=@('PromptDesk.exe','PromptDesk.exe.config','UpdatePromptDesk.ps1','seed.xml','LEEME.txt','INSTALAR_O_ACTUALIZAR.vbs','INICIAR.vbs','INICIAR_ALTERNATIVO.cmd')
foreach($name in $names){Copy-Item (Join-Path $root ('app/'+$name)) (Join-Path $runtime $name)}
$full=Join-Path $out 'PromptDesk_Windows.zip';Compress-Archive -Path (Join-Path $out 'full/PromptDesk') -DestinationPath $full -Force
# The first release recognizes the previously shipped binaries by hash.
$bases=@(Get-Content updates/legacy-bases.json -Raw|ConvertFrom-Json)
# Subsequent releases compare against the last complete runtime, keeping patches small.
$prior=Join-Path $out 'prior';New-Item $prior -ItemType Directory -Force|Out-Null
if($releaseList.Count -gt 0){
 $last=$releaseList[0].tagName
 & gh release download $last --repo $repo --pattern PromptDesk_Windows.zip --dir $prior
 if($LASTEXITCODE -ne 0){throw 'No se pudo obtener la versión anterior para comparar archivos.'}
 Expand-Archive (Join-Path $prior 'PromptDesk_Windows.zip') (Join-Path $prior 'expanded')
 $oldRoot=Join-Path $prior 'expanded/PromptDesk';$oldFiles=@{}
 foreach($name in $names){$p=Join-Path $oldRoot $name;if(Test-Path -LiteralPath $p){$oldFiles[$name]=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}}
 $bases=@([PSCustomObject]@{version=$last;files=[PSCustomObject]$oldFiles})
}
$patchRoot=Join-Path $out 'patch';$payload=Join-Path $patchRoot 'payload';New-Item $payload -ItemType Directory -Force|Out-Null
$files=@();$exeBases=@()
foreach($base in $bases){if($base.files.'PromptDesk.exe'){$exeBases+= [string]$base.files.'PromptDesk.exe'}}
foreach($name in @('PromptDesk.exe','UpdatePromptDesk.ps1')){
 $path=Join-Path $runtime $name;$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();$before=@()
 foreach($base in $bases){$property=$base.files.PSObject.Properties[$name];if($property){$before+=[string]$property.Value}else{$before+=''}}
 # Include only changed files relative to the latest supported baseline. Legacy hashes
 # remain valid for those files; cumulative updates for older bases need explicit testing.
 $lastBase=$bases[$bases.Count-1];$latestProp=$lastBase.files.PSObject.Properties[$name]
 if(@($bases|Where-Object { !$_.files.PSObject.Properties[$name] -or $_.files.PSObject.Properties[$name].Value -ne $hash }).Count -eq 0){continue}
 Copy-Item -LiteralPath $path -Destination (Join-Path $payload $name)
 $files+=@{path=$name;sha256=$hash;beforeSha256=@($before|Select-Object -Unique)}
}
$newExe=(Get-FileHash (Join-Path $runtime 'PromptDesk.exe') -Algorithm SHA256).Hash.ToLowerInvariant();$exeBases+=$newExe
@{product='PromptDesk';format=1;patchId=$version;baseExeSha256=@($exeBases|Select-Object -Unique);files=$files}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $patchRoot 'patch.json') -Encoding UTF8
Copy-Item app/UpdatePromptDesk.ps1 $patchRoot
Copy-Item scripts/APLICAR_PARCHE.vbs $patchRoot
$patch=Join-Path $out 'PromptDesk_Parche.zip';Compress-Archive -Path (Join-Path $patchRoot '*') -DestinationPath $patch -Force
@{product='PromptDesk';version=$version;url="https://github.com/$repo/releases/download/$tag/PromptDesk_Parche.zip";sha256=(Get-FileHash $patch -Algorithm SHA256).Hash.ToLowerInvariant()}|ConvertTo-Json|Set-Content (Join-Path $out 'latest.json') -Encoding UTF8
$notes=@"
PromptDesk $version

- Código completo y recursos versionados en este repositorio.
- Actualización desde GitHub con canal predeterminado.
- Instalación nueva: PromptDesk_Windows.zip.
- Instalación existente: Actualizar > Buscar actualizaciones.
- El catálogo y los respaldos del usuario se conservan en su PC.
"@
$notes|Set-Content (Join-Path $out 'notes.txt') -Encoding UTF8
& gh release create $tag $full $patch (Join-Path $out 'latest.json') --repo $repo --target $env:GITHUB_SHA --title "PromptDesk $version" --notes-file (Join-Path $out 'notes.txt') --latest
if($LASTEXITCODE -ne 0){throw 'No se pudo publicar la release.'}
