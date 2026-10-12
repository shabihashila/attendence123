[CmdletBinding()]
param([switch]$SkipUi)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$artifacts=Join-Path $root artifacts
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$services=@(
    @{Name='FegHR';Folder='FegHR/src/Host/FegHR.Api'},
    @{Name='FegCommunication';Folder='SharedServices/FegCommunication.WebApi'},
    @{Name='FegStorage';Folder='SharedServices/FegStorage.WebApi'}
)
$started=@()
foreach($service in $services){
    $process=Start-Process dotnet -ArgumentList 'run --no-build --launch-profile http' -WorkingDirectory "$root/$($service.Folder)" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$artifacts/$($service.Name).stdout.log" -RedirectStandardError "$artifacts/$($service.Name).stderr.log"
    $started+=@{Name=$service.Name;ProcessId=$process.Id}
}
if(!$SkipUi){
    $process=Start-Process cmd.exe -ArgumentList '/c npm.cmd start' -WorkingDirectory "$root/FegHR/Ui/FegHR.Ui" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$artifacts/ui.stdout.log" -RedirectStandardError "$artifacts/ui.stderr.log"
    $started+=@{Name='FegHR.Ui';ProcessId=$process.Id}
}
$started | ConvertTo-Json | Set-Content "$artifacts/local-processes.json"
Write-Output 'Starting FegHR http://localhost:4425, API http://localhost:5975, communication :5185, storage :5285.'
