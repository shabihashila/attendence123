<#
.SYNOPSIS
Creates db_feg_hr and applies the versioned scripts.
.DESCRIPTION
Refuses existing targets and never drops a database. A failed target is retained for
inspection. Use deploy-db.ps1 for a repeat deployment to a database you own.
#>
[CmdletBinding()]
param(
    [string]$Server='localhost',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,119}$')][string]$Database='db_feg_hr',
    [ValidatePattern('^[^@\s'']+@[^@\s'']+\.[^@\s'']+$')][string]$PlatformOwnerEmail,
    [string]$PlatformOwnerName='Product Owner',
    [string]$PlatformOwnerPasswordHash
)
$ErrorActionPreference='Stop'
if($Database -in @('master','model','msdb','tempdb','db_agent_sessions')){throw 'Protected databases are not targets.'}
Get-Command sqlcmd -ErrorAction Stop | Out-Null
$exists=& sqlcmd -S $Server -d master -E -C -b -I -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$Database') IS NULL THEN 0 ELSE 1 END;"
if($LASTEXITCODE -ne 0){throw 'Database preflight failed.'}
if(($exists -join '').Trim() -ne '0'){throw 'The database already exists. No changes were made; use deploy-db.ps1 for an authorized repeat deployment.'}
& sqlcmd -S $Server -d master -E -C -b -I -Q "CREATE DATABASE [$Database];"
if($LASTEXITCODE -ne 0){throw 'Database creation failed.'}
$deployParameters=@{Server=$Server;Database=$Database}
if($PlatformOwnerEmail){$deployParameters.PlatformOwnerEmail=$PlatformOwnerEmail;$deployParameters.PlatformOwnerName=$PlatformOwnerName}
if($PlatformOwnerPasswordHash){$deployParameters.PlatformOwnerPasswordHash=$PlatformOwnerPasswordHash}
& (Join-Path $PSScriptRoot 'deploy-db.ps1') @deployParameters
