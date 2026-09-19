param([string]$Compiler="${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try{
 if(!(Test-Path -LiteralPath $Compiler)){throw 'Укажите путь к Inno Setup ISCC.exe через -Compiler.'}
 & "$PSScriptRoot/prepare-components.ps1"
 dotnet run --project tests/InstallerTests -c Release
 if($LASTEXITCODE -ne 0){throw 'Проверки установщика не прошли'}
 foreach($project in @('ZapretDesktop','ZapretService')){
  $dest=if($project -eq 'ZapretDesktop'){'artifacts/app'}else{'artifacts/app/service'}
  dotnet publish "src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $dest
  if($LASTEXITCODE -ne 0){throw "Сборка $project завершилась ошибкой"}
 }
 & $Compiler 'installer/Zapret.iss'
 if($LASTEXITCODE -ne 0){throw 'Сборка установщика завершилась ошибкой'}
 $hash=(Get-FileHash 'artifacts/release/ZapretSetup.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
 "$hash  ZapretSetup.exe" | Set-Content 'artifacts/release/SHA256SUMS.txt' -Encoding ascii
}finally{Pop-Location}
