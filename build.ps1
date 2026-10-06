$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'WPF\WindowsBase.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')), ('/out:' + (Join-Path $PSScriptRoot 'HdrControlCenter.exe')))
$iconPath = Join-Path $PSScriptRoot 'assets\ControlCenter.ico'
if (Test-Path -LiteralPath $iconPath) { $arguments += '/win32icon:' + $iconPath }
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $framework $reference) }
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output (Join-Path $PSScriptRoot 'HdrControlCenter.exe')
