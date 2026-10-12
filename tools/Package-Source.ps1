[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root 'FEG-MICROSERVICES-source.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($output,[IO.FileMode]::Create)
$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
function Add-SourceFolder([string]$folder){
    foreach($entry in Get-ChildItem -LiteralPath $folder -Force){
        if($entry.Name -in @('artifacts','node_modules','bin','obj','dist','.angular','.vs','.git','App_Data')){continue}
        if($entry.PSIsContainer){Add-SourceFolder $entry.FullName;continue}
        if($entry.Name -in @('appsettings.Development.json','appsettings.Production.json','FEG-MICROSERVICES-source.zip')){continue}
        $relative=$entry.FullName.Substring($root.Length+1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.FullName,"FEG-MICROSERVICES/$relative",[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
try {Add-SourceFolder $root} finally {$zip.Dispose();$stream.Dispose()}
Write-Output $output
