<#
.SYNOPSIS
Adds demo data to one FegHR company: a sign-in for every role, ten employees, setup rows and a
month of attendance and leave.
.DESCRIPTION
Local development only: refuses any server other than this machine. -PasswordHash is one BCrypt
hash used for all four demo sign-ins (admin.demo, hr.demo, manager.demo, employee.demo
@feghr.test). Running it again changes nothing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$TenantSlug,
    [Parameter(Mandatory)][ValidatePattern('^\$2[aby]\$\d\d\$[./A-Za-z0-9]{53}$')][string]$PasswordHash,
    [ValidateSet('localhost','.','(local)')][string]$Server='localhost',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,119}$')][string]$Database='db_feg_hr'
)
$ErrorActionPreference='Stop'
Get-Command sqlcmd -ErrorAction Stop | Out-Null
# sqlcmd reads its script variables from the process environment; restored in finally.
$prior=@($env:DemoTenantSlug,$env:DemoPasswordHash)
try {
    $env:DemoTenantSlug=$TenantSlug
    $env:DemoPasswordHash=$PasswordHash
    & sqlcmd -S $Server -d $Database -E -C -b -I -W -i (Join-Path $PSScriptRoot '01_DemoData.sql')
    if($LASTEXITCODE -ne 0){throw 'The demo seed failed; nothing was committed.'}
} finally {$env:DemoTenantSlug,$env:DemoPasswordHash=$prior}
