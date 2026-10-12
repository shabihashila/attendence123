<# Creates and seeds the three FEG databases without dropping or resetting existing data. #>
[CmdletBinding()]
param([ValidateSet('localhost','.','(local)')][string]$Server='localhost',[switch]$SkipUiInstall)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Get-Command sqlcmd,dotnet,node -ErrorAction Stop | Out-Null
function Run-Sql([string]$Database,[string]$File){
    & sqlcmd -S $Server -d $Database -E -C -b -I -i $File
    if($LASTEXITCODE -ne 0){throw "SQL failed: $File"}
}
# Generate the explicitly requested demo password using BCrypt cost 12.
& dotnet build "$root/FegHR/tools/PasswordHash/PasswordHash.csproj" --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Password hasher build failed.'}
$hash=(& dotnet "$root/FegHR/tools/PasswordHash/bin/Debug/net10.0/PasswordHash.dll" | Select-Object -Last 1).Trim()
if($hash -notmatch '^\$2[aby]\$12\$[./A-Za-z0-9]{53}$'){throw 'Invalid BCrypt hash.'}
foreach($database in @('db_feg_hr','db_feg_communication','db_feg_storage')){
    & sqlcmd -S $Server -d master -E -C -b -Q "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database];"
    if($LASTEXITCODE -ne 0){throw "Database creation failed: $database"}
}
& "$root/FegHR/Database/deploy-db.ps1" -Server $Server -Database db_feg_hr -PlatformOwnerEmail 'Administrator@feghr.com' -PlatformOwnerName 'FegHR Administrator' -PlatformOwnerPasswordHash $hash
Run-Sql db_feg_communication "$root/Database/FegCommunication.sql"
Run-Sql db_feg_storage "$root/Database/FegStorage.sql"
$previousHash=$env:DemoPasswordHash
try {
    $env:DemoPasswordHash=$hash
    Run-Sql db_feg_hr "$root/Database/Seed-Companies.sql"
    foreach($slug in @('lighthouse-consulting','fegtechnova','futurepathglobal')){
        & "$root/FegHR/Database/demo-seed/Seed-Demo.ps1" -Server $Server -Database db_feg_hr -TenantSlug $slug -PasswordHash $hash
    }
    Run-Sql db_feg_hr "$root/Database/Seed-OwnerEmployee.sql"
    Run-Sql db_feg_hr "$root/Database/Seed-Brand.sql"
    Run-Sql db_feg_hr "$root/Database/Seed-CompanyBranding.sql"
    Run-Sql db_feg_communication "$root/Database/Seed-SharedDemo.sql"
    Run-Sql db_feg_storage "$root/Database/Seed-SharedDemo.sql"
} finally {$env:DemoPasswordHash=$previousHash}
function Random-Key {
    $bytes=New-Object byte[] 48
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try {$rng.GetBytes($bytes);[Convert]::ToBase64String($bytes)} finally {$rng.Dispose()}
}
function Save-Json($value,[string]$path){$value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8}
$apiSettings="$root/FegHR/src/Host/FegHR.Api/appsettings.Development.json"
if(Test-Path $apiSettings){$hr=Get-Content $apiSettings -Raw | ConvertFrom-Json}
else {
    $hr=Get-Content "$root/FegHR/src/Host/FegHR.Api/appsettings.Development.example.json" -Raw | ConvertFrom-Json
    $hr.Jwt.SigningKey=Random-Key
    $hr.FileStorageService.ApiKey=Random-Key
    $hr.CommunicationService.ApiKey=Random-Key
    Save-Json $hr $apiSettings
}
foreach($entry in @(@{Name='FegStorage';Key=$hr.FileStorageService.ApiKey},@{Name='FegCommunication';Key=$hr.CommunicationService.ApiKey})){
    $path="$root/SharedServices/$($entry.Name).WebApi/appsettings.Development.json"
    if(!(Test-Path $path)){
        $settings=@{ApiKey=@{Clients=@{FegHR=$entry.Key}}}
        if($entry.Name -eq 'FegStorage'){$settings.LocalBackend=@{SigningKey=(Random-Key)}}
        Save-Json $settings $path
    }
}
$demoFolder="$root/SharedServices/FegStorage.WebApi/App_Data/storage/FegHR/feghr/demo"
New-Item -ItemType Directory -Force -Path $demoFolder | Out-Null
if(!(Test-Path "$demoFolder/readme.txt")){[IO.File]::WriteAllText("$demoFolder/readme.txt",'FegHR demo storage ready.')}
if(!$SkipUiInstall){
    Push-Location "$root/FegHR/Ui/FegHR.Ui"
    try {& npm.cmd ci --no-audit --no-fund;if($LASTEXITCODE -ne 0){throw 'npm ci failed.'}} finally {Pop-Location}
}
Write-Output 'FegHR local setup complete. Owner: Administrator@feghr.com / Admin123@!'
