<#
.SYNOPSIS
Applies the FegHR versioned scripts to an existing database.
.DESCRIPTION
Runs transactionally and fails on SQL errors; every script is idempotent, so a repeat run is
safe. Windows authentication by default; -SqlCredential uses a SQL login instead (hosted
servers), passing the password to sqlcmd through SQLCMDPASSWORD, never on the command line.
-PlatformOwnerEmail creates or promotes the product owner; -PlatformOwnerPasswordHash is the
BCrypt cost-12 hash for a new account (an existing account keeps its password).
#>
[CmdletBinding()]
param(
    [string]$Server='localhost',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,119}$')][string]$Database='db_feg_hr',
    [ValidatePattern('^[^@\s'']+@[^@\s'']+\.[^@\s'']+$')][string]$PlatformOwnerEmail,
    [ValidatePattern('^[\p{L}\p{N} .\-]{2,150}$')][string]$PlatformOwnerName='Product Owner',
    [ValidatePattern('^\$2[aby]\$12\$[./A-Za-z0-9]{53}$')][string]$PlatformOwnerPasswordHash,
    # Marks this company as the owner company: no licence needed, cannot be suspended.
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$OwnerCompanySlug,
    [Management.Automation.PSCredential]$SqlCredential
)
$ErrorActionPreference='Stop'
if($Database -in @('master','model','msdb','tempdb','db_agent_sessions')){throw 'Protected databases are not targets.'}
Get-Command sqlcmd -ErrorAction Stop | Out-Null
$authArgs=@(if($SqlCredential){'-U',$SqlCredential.UserName}else{'-E'})
$priorSqlPassword=$env:SQLCMDPASSWORD
if($SqlCredential){$env:SQLCMDPASSWORD=$SqlCredential.GetNetworkCredential().Password}
$seedTimestamp=[DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffzzz',[Globalization.CultureInfo]::InvariantCulture)
function Invoke-ReleaseFile([string]$File){
    & sqlcmd -S $Server -d $Database $authArgs -C -b -V 11 -I -i $File
    if($LASTEXITCODE -ne 0){throw "Deployment failed in $File. Inspect the retained database before retrying."}
}
$priorSeedTimestamp=$env:SeedTimestamp
$env:SeedTimestamp=$seedTimestamp
Push-Location (Join-Path $PSScriptRoot 'release-1.0')
try {
    Invoke-ReleaseFile '00_Deploy.sql'
    if($PlatformOwnerEmail){
        $priorOwner=@($env:PlatformOwnerEmail,$env:PlatformOwnerName,$env:PlatformOwnerPasswordHash)
        try {
            $env:PlatformOwnerEmail=$PlatformOwnerEmail
            $env:PlatformOwnerName=$PlatformOwnerName
            # sqlcmd reads its script variables from the process environment; restored in finally.
            $env:PlatformOwnerPasswordHash=if($PlatformOwnerPasswordHash){$PlatformOwnerPasswordHash}else{'none'}
            Invoke-ReleaseFile 'SeedData/91_PlatformOwner.sql'
        } finally {$env:PlatformOwnerEmail,$env:PlatformOwnerName,$env:PlatformOwnerPasswordHash=$priorOwner}
    }
    if($OwnerCompanySlug){
        $priorSlug=$env:OwnerCompanySlug
        try { $env:OwnerCompanySlug=$OwnerCompanySlug; Invoke-ReleaseFile 'SeedData/92_OwnerCompany.sql' } finally {$env:OwnerCompanySlug=$priorSlug}
    }
} finally {Pop-Location; $env:SeedTimestamp=$priorSeedTimestamp; $env:SQLCMDPASSWORD=$priorSqlPassword}
Write-Output "FegHR deployed: $Server / $Database"
