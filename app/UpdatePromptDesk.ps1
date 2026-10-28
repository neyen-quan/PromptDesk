param([string]$PatchFile='', [string]$Target='', [switch]$LibraryOnly,[string]$RequestFile='',[switch]$RestartAfterApply)
$ErrorActionPreference='Stop'
function Hash-File([string]$Path) { if(Test-Path -LiteralPath $Path -PathType Leaf){ return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }; return '' }
function Safe-Path([string]$Root,[string]$Relative) {
 if($Relative -notmatch '^(?:assets/)?[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*$' -or $Relative -match '(?i)(datos|seed|patch-state)\.xml$' -or $Relative -notmatch '\.(exe|dll|config|png|ico|ps1|vbs|txt)$'){throw "Ruta de parche no permitida: $Relative"}
 $p=Join-Path $Root $Relative
 $walk=$p
 while($walk -and $walk.Length -ge $Root.Length){if((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'No se admiten enlaces en la ruta de actualización.'};$walk=Split-Path $walk -Parent}
 return $p
}
function Atomic-Copy([string]$Source,[string]$Destination) {
 $parent=Split-Path $Destination -Parent;[IO.Directory]::CreateDirectory($parent)|Out-Null
 $temp=$Destination+'.incoming-'+[Guid]::NewGuid().ToString('N')
 try{[IO.File]::Copy($Source,$temp,$true);if([IO.File]::Exists($Destination)){[IO.File]::Replace($temp,$Destination,$temp+'.old')}else{[IO.File]::Move($temp,$Destination)}}finally{if([IO.File]::Exists($temp+'.old')){[IO.File]::Delete($temp+'.old')};if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Apply-Patch([string]$ManifestPath,[string]$InstallDir) {
 $InstallDir=[IO.Path]::GetFullPath($InstallDir);$ManifestPath=[IO.Path]::GetFullPath($ManifestPath)
 $m=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
 if($m.product -ne 'PromptDesk' -or $m.format -ne 1 -or $m.patchId -notmatch '^[A-Za-z0-9._-]{1,80}$'){throw 'El paquete no es un parche válido de PromptDesk.'}
 if(@($m.files).Count -eq 0 -or @($m.files).Count -gt 100){throw 'Lista de archivos inválida.'}
 $exe=Join-Path $InstallDir 'PromptDesk.exe'
 if(!(Test-Path -LiteralPath $exe -PathType Leaf)){throw 'La carpeta elegida no contiene PromptDesk.exe.'}
 if(@($m.baseExeSha256) -notcontains (Hash-File $exe)){throw 'Este parche no corresponde a tu versión. No se modificó ningún archivo.'}
 $payload=Join-Path (Split-Path $ManifestPath -Parent) 'payload';$plan=@();$seen=@{}
 foreach($f in $m.files){
  $name=[string]$f.path;if($seen.ContainsKey($name.ToLowerInvariant())){throw 'Archivo duplicado en el parche.'};$seen[$name.ToLowerInvariant()]=$true
  $src=Safe-Path $payload $name;$dst=Safe-Path $InstallDir $name
  if($f.sha256 -notmatch '^[a-f0-9]{64}$' -or (Hash-File $src) -ne $f.sha256){throw "Archivo incompleto o alterado: $name"}
  $old=Hash-File $dst
  if($old -eq $f.sha256){continue}
  if(@($f.beforeSha256) -notcontains $old){throw "El archivo $name no coincide con la versión esperada. No se modificó nada."}
  $plan+= [PSCustomObject]@{Name=$name;Source=$src;Destination=$dst;Existed=($old -ne '');Hash=$f.sha256}
 }
 if($plan.Count -eq 0){return 'Este parche ya está aplicado.'}
 $work=Join-Path ([IO.Path]::GetTempPath()) ('PromptDesk-stage-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($work)|Out-Null
 $backup=Join-Path $InstallDir ('Updates/Backups/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
 $changed=@();$state=Join-Path $InstallDir 'patch-state.json';$hadState=Test-Path -LiteralPath $state
 try{
  [IO.Directory]::CreateDirectory($backup)|Out-Null
  foreach($p in $plan){$stage=Join-Path $work $p.Name;[IO.Directory]::CreateDirectory((Split-Path $stage -Parent))|Out-Null;[IO.File]::Copy($p.Source,$stage);if((Hash-File $stage) -ne $p.Hash){throw 'Falló la verificación de la copia temporal.'};if($p.Existed){$b=Join-Path $backup $p.Name;[IO.Directory]::CreateDirectory((Split-Path $b -Parent))|Out-Null;[IO.File]::Copy($p.Destination,$b)}}
  if($hadState){[IO.File]::Copy($state,(Join-Path $backup 'patch-state.json'))}
  $plan|Select-Object Name,Existed|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $backup 'replaced-files.json') -Encoding UTF8
  foreach($p in $plan){$changed+=,$p;Atomic-Copy (Join-Path $work $p.Name) $p.Destination;if((Hash-File $p.Destination) -ne $p.Hash){throw "Falló la verificación final: $($p.Name)"}}
  @{product='PromptDesk';patchId=$m.patchId;installedAt=(Get-Date).ToString('o');backup=$backup;exeSha256=(Hash-File $exe)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $work 'patch-state.json') -Encoding UTF8
  Atomic-Copy (Join-Path $work 'patch-state.json') $state
 }catch{
  $cause=$_.Exception.Message;$restoreErrors=@()
  foreach($p in $changed){try{if($p.Existed){Atomic-Copy (Join-Path $backup $p.Name) $p.Destination}elseif(Test-Path -LiteralPath $p.Destination){Remove-Item -LiteralPath $p.Destination -Force}}catch{$restoreErrors+=$p.Name}}
  try{if($hadState -and (Test-Path -LiteralPath (Join-Path $backup 'patch-state.json'))){Atomic-Copy (Join-Path $backup 'patch-state.json') $state}elseif(!$hadState -and (Test-Path -LiteralPath $state)){Remove-Item -LiteralPath $state -Force}}catch{$restoreErrors+='patch-state.json'}
  if($restoreErrors.Count){throw "Actualización interrumpida: $cause. Recuperación pendiente: $($restoreErrors -join ', '). Copias originales: $backup"}
  throw "No se aplicó el parche. Se conservaron/restauraron los archivos anteriores. $cause"
 }finally{if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force}}
 return "Parche $($m.patchId) aplicado. Archivos actualizados: $($plan.Count). Tus prompts y ajustes se conservan."
}
function Expand-PatchZip([string]$Zip,[string]$Destination){
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 if((Get-Item -LiteralPath $Zip).Length -gt 128MB){throw 'El parche supera el límite de tamaño.'}
 $archive=[IO.Compression.ZipFile]::OpenRead($Zip)
 try{
  $total=0L;$seen=@{};if($archive.Entries.Count -gt 1000){throw 'Demasiados archivos en el parche.'}
  foreach($e in $archive.Entries){$n=$e.FullName.Replace('\','/');if($n.StartsWith('/') -or $n.Contains(':') -or $n.Split('/') -contains '..'){throw 'Ruta inválida dentro del ZIP.'};if(($e.ExternalAttributes -shr 16 -band 61440) -eq 40960){throw 'El ZIP contiene un enlace.'};$total+=$e.Length;if($total -gt 256MB){throw 'El ZIP expandido supera el límite.'};if($n.EndsWith('/')){continue};if($seen.ContainsKey($n.ToLowerInvariant())){throw 'Archivo duplicado en el ZIP.'};$seen[$n.ToLowerInvariant()]=$true;$dest=Join-Path $Destination $n;[IO.Directory]::CreateDirectory((Split-Path $dest -Parent))|Out-Null;$input=$e.Open();$output=[IO.File]::Create($dest);try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}}
 }finally{$archive.Dispose()}
 $manifests=@(Get-ChildItem -LiteralPath $Destination -Recurse -Filter patch.json);if($manifests.Count -ne 1){throw 'El ZIP debe contener un único patch.json.'};return $manifests[0].FullName
}
function Receive-Https([string]$Url,[string]$Destination,[long]$Limit){
 $uri=[Uri]$Url;if($uri.Scheme -ne 'https' -or $uri.UserInfo){throw 'La descarga requiere HTTPS.'}
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 $request=[Net.HttpWebRequest]::Create($uri);$request.Timeout=20000;$request.ReadWriteTimeout=20000;$request.UserAgent='PromptDesk-Updater';$response=$request.GetResponse()
 try{if($response.ResponseUri.Scheme -ne 'https'){throw 'Redirección de descarga no segura.'};if($response.ContentLength -gt $Limit){throw 'Descarga demasiado grande.'};$stream=$response.GetResponseStream();$file=[IO.File]::Create($Destination);try{$buffer=New-Object byte[] 65536;$total=0L;while(($count=$stream.Read($buffer,0,$buffer.Length)) -gt 0){$total+=$count;if($total -gt $Limit){throw 'Descarga demasiado grande.'};$file.Write($buffer,0,$count)}}finally{$stream.Dispose();$file.Dispose()}}finally{$response.Dispose()}
}
if($LibraryOnly){return}
Add-Type -AssemblyName System.Windows.Forms
if($RequestFile){
 $session=Split-Path $RequestFile -Parent;$restart=$false
 try{
  [xml]$req=Get-Content -LiteralPath $RequestFile -Raw;$Target=[string]$req.update.target;$mode=[string]$req.update.mode;$value=[string]$req.update.value
  if($mode -eq 'feed'){
   $feedFile=Join-Path $session 'feed.json';Receive-Https $value $feedFile 1MB;$feed=Get-Content -LiteralPath $feedFile -Raw|ConvertFrom-Json
   if($feed.product -ne 'PromptDesk' -or $feed.sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Canal de versiones inválido.'}
   $installed=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Target 'PromptDesk.exe')).FileVersion
   if([version]$feed.version -le [version]$installed){[Windows.Forms.MessageBox]::Show('Ya tenés la última versión publicada.','PromptDesk')|Out-Null;exit}
   $zip=Join-Path $session 'download.zip';Receive-Https ([string]$feed.url) $zip 128MB;if((Hash-File $zip) -ne $feed.sha256){throw 'La descarga no coincide con la versión publicada.'}
  }elseif($mode -eq 'zip'){$zip=$value}else{throw 'Modo de actualización inválido.'}
  $manifest=Expand-PatchZip $zip (Join-Path $session 'expanded')
  $m=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
  if($m.product -ne 'PromptDesk' -or @($m.baseExeSha256) -notcontains (Hash-File (Join-Path $Target 'PromptDesk.exe'))){throw 'El parche no corresponde a tu versión de PromptDesk.'}
  if([Windows.Forms.MessageBox]::Show('El parche está listo. Se guardarán tus cambios y se reiniciará PromptDesk. ¿Continuar?','Actualizar PromptDesk','YesNo','Question') -ne 'Yes'){exit}
  [IO.File]::WriteAllText((Join-Path $session 'ready'),'ready');$deadline=[DateTime]::UtcNow.AddSeconds(60)
  while(Get-Process -Id ([int]$req.update.pid) -ErrorAction SilentlyContinue){if(Test-Path (Join-Path $session 'cancel')){throw 'No se pudo guardar. Actualización cancelada.'};if([DateTime]::UtcNow -gt $deadline){throw 'PromptDesk no se cerró a tiempo.'};Start-Sleep -Milliseconds 150}
  $restart=$true;$mutex=New-Object Threading.Mutex($false,'Local\PromptDesk_Neyen_v1');$held=$false
  try{$held=$mutex.WaitOne(0);if(!$held){throw 'Otra instancia de PromptDesk está abierta.'};$result=Apply-Patch $manifest $Target}finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
  [Windows.Forms.MessageBox]::Show($result,'PromptDesk')|Out-Null
 }catch{[Windows.Forms.MessageBox]::Show($_.Exception.Message,'No se pudo actualizar PromptDesk')|Out-Null}
 finally{if($restart){Start-Process -FilePath (Join-Path $Target 'PromptDesk.exe') -WorkingDirectory $Target};if(Test-Path (Join-Path $session 'expanded')){Remove-Item (Join-Path $session 'expanded') -Recurse -Force};if(Test-Path (Join-Path $session 'download.zip')){Remove-Item (Join-Path $session 'download.zip') -Force}}
 exit
}
$mutex=$null;$held=$false;$success=$false
try{
 if(!$PatchFile){$near=Join-Path $PSScriptRoot 'patch.json';if(Test-Path -LiteralPath $near){$PatchFile=$near}else{$pick=New-Object Windows.Forms.OpenFileDialog;$pick.Title='Elegí patch.json del parche extraído';$pick.Filter='Parche PromptDesk (patch.json)|patch.json';if($pick.ShowDialog() -ne 'OK'){exit};$PatchFile=$pick.FileName;$pick.Dispose()}}
 if(!$Target -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'PromptDesk.exe'))){$Target=$PSScriptRoot}
 if(!$Target){try{$Target=(Get-ItemProperty 'HKCU:\Software\PromptDesk' -Name InstallDir -ErrorAction Stop).InstallDir}catch{}}
 if(!$Target){$Target=Join-Path $env:LOCALAPPDATA 'Programs\PromptDesk'}
 if(!(Test-Path -LiteralPath (Join-Path $Target 'PromptDesk.exe'))){$pick=New-Object Windows.Forms.OpenFileDialog;$pick.Title='Ubicá el PromptDesk.exe que usás actualmente';$pick.Filter='PromptDesk.exe|PromptDesk.exe';if($pick.ShowDialog() -ne 'OK'){exit};$Target=Split-Path $pick.FileName -Parent;$pick.Dispose()}
 $mutex=New-Object Threading.Mutex($false,'Local\PromptDesk_Neyen_v1');try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true};if(!$held){throw 'Cerrá PromptDesk con la X y volvé a aplicar el parche.'}
 $result=Apply-Patch $PatchFile $Target
 try{New-Item 'HKCU:\Software\PromptDesk' -Force|Out-Null;New-ItemProperty 'HKCU:\Software\PromptDesk' -Name InstallDir -Value $Target -PropertyType String -Force|Out-Null}catch{$result+="`nNo se pudo recordar la carpeta. Podés ejecutar ACTUALIZAR_PROMPTDESK.vbs desde la carpeta del programa."}
 [Windows.Forms.MessageBox]::Show($result,'PromptDesk · Actualización')|Out-Null
 $success=$true
}catch{[Windows.Forms.MessageBox]::Show($_.Exception.Message,'PromptDesk · No se pudo actualizar')|Out-Null;exit 1}finally{if($held){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}

if($success -and $RestartAfterApply){Start-Process -FilePath (Join-Path $Target 'PromptDesk.exe') -WorkingDirectory $Target}
