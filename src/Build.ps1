param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\outputs\小黑手帐.exe'),
    [string]$Main = 'Xiaohei.PetApp',
    [string[]]$ExtraSource,
    [switch]$Console
)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System','System.Core','System.Xml','System.Xml.Linq','System.Runtime.Serialization','System.Drawing','System.Windows.Forms','System.Xaml','WindowsBase','PresentationCore','PresentationFramework','UIAutomationTypes','UIAutomationProvider','UIAutomationClient')
$arguments = @('/nologo','/optimize+','/platform:anycpu','/codepage:65001',"/out:$Destination","/main:$Main","/win32manifest:$PSScriptRoot\App.manifest")
if ($Console) { $arguments += '/target:exe' } else { $arguments += '/target:winexe' }
if (Test-Path -LiteralPath "$PSScriptRoot\Pet.ico") { $arguments += "/win32icon:$PSScriptRoot\Pet.ico" }
foreach ($ref in $refs) {
    $dll = "$framework\$ref.dll"
    if (!(Test-Path -LiteralPath $dll)) { $dll = "$framework\WPF\$ref.dll" }
    $arguments += "/reference:$dll"
}
$sourceFiles = @('Store.cs','HolidayCalendar.cs','PetArt.cs','TailLitter.cs','SpiritOrbit.cs','PetActor.cs','PetDock.cs','PetLayout.cs','PetMenu.cs','Planner.cs','PlannerDraft.cs','PlannerWindow.cs','PetApp.cs','AssemblyInfo.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& "$framework\csc.exe" @arguments @sourceFiles @ExtraSource
exit $LASTEXITCODE
