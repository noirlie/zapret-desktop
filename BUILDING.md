# Сборка из исходников

Нужны Windows 10/11 x64, .NET SDK 8 и Inno Setup 6. Для проверки/сборки UI права администратора не нужны. Установщик потребует UAC при установке службы.

## Сборка приложения

Из корня репозитория:

```powershell
dotnet build src/ZapretDesktop/ZapretDesktop.csproj -c Release
dotnet build src/ZapretService/ZapretService.csproj -c Release
```

## Сборка установщика

```powershell
./installer/build.ps1 -Compiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Скрипт загружает официальный пакет Flowseal 1.10.2, проверяет фиксированный SHA-256, запускает тесты установщика и публикует self-contained приложения с .NET. Результат: `artifacts/release/ZapretSetup.exe` и `SHA256SUMS.txt`. Другие версии компонентов требуют осознанного изменения версии и контрольной суммы в prepare-components.ps1.

## Тесты

```powershell
./installer/prepare-components.ps1
$projects = 'ImporterTests','CoreTests','ServiceTests','RecoveryTests','UpdateTests','ComponentTests','StartupTests','ProcessTests','ExitTests','InstallerTests','UiTests'
foreach ($project in $projects) {
    dotnet run --project "tests/$project" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Failed: $project" }
}
```

Большинство тестов работают с временными файлами и тестовым IPC, не устанавливая службу и не запуская winws. UiTests рендерит WPF и проверяет трей, поэтому нужен интерактивный Windows-сеанс; не запускайте одновременно несколько экземпляров этих тестов.

`tests/ServiceSmoke` — отдельная проверка установленной настоящей службы. Без параметров читает состояние; `--manual` и `--auto` действительно запускают и останавливают подключение. Не включена в обычный цикл тестов.

Наличие исходников и успешной сборки не означает побитовую воспроизводимость готового EXE: версия SDK и инструменты упаковки влияют на результат. Готовый установщик не подписан сертификатом издателя.
