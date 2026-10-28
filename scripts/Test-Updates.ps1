$ErrorActionPreference='Stop'
. "$PSScriptRoot/../app/UpdatePromptDesk.ps1" -LibraryOnly
function Assert($v,$m){if(!$v){throw $m}}
$root=Join-Path $PSScriptRoot 'test-install';$pkg=Join-Path $PSScriptRoot 'test-pkg';Remove-Item $root,$pkg -Recurse -Force -ErrorAction SilentlyContinue
New-Item $root,(Join-Path $pkg 'payload') -ItemType Directory -Force|Out-Null
Set-Content (Join-Path $root 'PromptDesk.exe') 'EXE';Set-Content (Join-Path $root 'datos.xml') 'MY DATA';Set-Content (Join-Path $root 'one.txt') 'OLD';Set-Content (Join-Path $pkg 'payload/one.txt') 'NEW';Set-Content (Join-Path $pkg 'payload/two.txt') 'SECOND'
$data=Hash-File (Join-Path $root 'datos.xml')
$m=@{product='PromptDesk';format=1;patchId='test';baseExeSha256=@((Hash-File (Join-Path $root 'PromptDesk.exe')));files=@(@{path='one.txt';sha256=(Hash-File (Join-Path $pkg 'payload/one.txt'));beforeSha256=@((Hash-File (Join-Path $root 'one.txt')))},@{path='two.txt';sha256=(Hash-File (Join-Path $pkg 'payload/two.txt'));beforeSha256=@('')})}
$manifest=Join-Path $pkg 'patch.json';$m|ConvertTo-Json -Depth 6|Set-Content $manifest
$orig=(Get-Item Function:Atomic-Copy).ScriptBlock
function Atomic-Copy($Source,$Destination){if($Destination -eq (Join-Path $root 'two.txt')){throw 'injected failure'};& $orig $Source $Destination}
$failed=$false;try{Apply-Patch $manifest $root}catch{$failed=$true};Assert $failed 'No injected failure';Assert ((Hash-File (Join-Path $root 'one.txt')) -eq $m.files[0].beforeSha256[0]) 'Rollback failed'
Set-Item Function:Atomic-Copy $orig
Apply-Patch $manifest $root;Assert ((Apply-Patch $manifest $root) -like '*ya est*') 'Repeat failed';Assert ((Hash-File (Join-Path $root 'datos.xml')) -eq $data) 'Data changed'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $PSScriptRoot 'sample.zip';Remove-Item $zip -Force -ErrorAction SilentlyContinue;[IO.Compression.ZipFile]::CreateFromDirectory($pkg,$zip)
$expanded=Join-Path $root 'extracted';$file=Expand-PatchZip $zip $expanded;Assert (Test-Path $file) 'Auto unzip failed'
foreach($case in @('version','hash','traversal')){$bad=$m|ConvertTo-Json -Depth 6|ConvertFrom-Json;if($case -eq 'version'){$bad.baseExeSha256=@('0'*64)};if($case -eq 'hash'){$bad.files[0].sha256='0'*64};if($case -eq 'traversal'){$bad.files[0].path='../datos.xml'};$bad|ConvertTo-Json -Depth 6|Set-Content $manifest;$failed=$false;try{Apply-Patch $manifest $root}catch{$failed=$true};Assert $failed "Accepted $case"}
'PASS ZIP extraction, apply, idempotence, rollback, data preservation, wrong version, hash, unsafe paths.'
Remove-Item $root,$pkg,$zip -Recurse -Force
