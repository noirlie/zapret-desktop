param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root 'artifacts/downloads'
New-Item -ItemType Directory -Force $cache | Out-Null
$archive=Join-Path $cache 'zapret-discord-youtube-1.10.2.zip'
$expected='5eaac9fb2e4b1abd693487452a3ff3f4dfe9578a45f9ddddfa4bc1f5a6bb62d5'
if(!(Test-Path $archive)){
 & curl.exe --fail --location --retry 2 'https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.2/zapret-discord-youtube-1.10.2.zip' --output $archive
 if($LASTEXITCODE -ne 0){throw 'Не удалось загрузить компоненты'}
}
if((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected){throw 'Контрольная сумма компонентов не совпадает'}
$unpack=Join-Path $cache 'unpacked'
Expand-Archive -LiteralPath $archive -DestinationPath $unpack -Force
$matches=@(Get-ChildItem $unpack -Filter general.bat -Recurse)
if($matches.Count -ne 1){throw 'Неоднозначная структура пакета'}
$source=$matches[0].DirectoryName
$target=Join-Path $PSScriptRoot 'payload/components'
New-Item -ItemType Directory -Force $target | Out-Null
foreach($dir in @('bin','lists')){Copy-Item -LiteralPath (Join-Path $source $dir) -Destination $target -Recurse -Force}
Copy-Item (Join-Path $source 'general*.bat') $target -Force
foreach($name in @('list-general-user.txt','list-exclude-user.txt','ipset-exclude-user.txt')){
 $path=Join-Path $target ('lists/'+$name)
 if(!(Test-Path $path)){[IO.File]::WriteAllText($path,'')}
}
@{Tag='1.10.2';Sha256=$expected} | ConvertTo-Json | Set-Content (Join-Path $target 'release.json') -Encoding utf8
Write-Host 'Компоненты 1.10.2 проверены и подготовлены.'
