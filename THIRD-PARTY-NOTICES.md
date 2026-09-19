# Сторонние компоненты

Лицензия MIT в корне распространяется на собственный код оболочки noirlie, но не заменяет лицензии перечисленных компонентов. Исходные бинарники компонентов не модифицируются оболочкой.

| Компонент | Источник / исходники | Лицензия |
|---|---|---|
| Скрипты, списки и пакет компонентов | [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube), [релиз 1.10.2](https://github.com/Flowseal/zapret-discord-youtube/releases/tag/1.10.2) | MIT и лицензии включённых зависимостей; `licenses/Flowseal-LICENSE.txt` |
| Движок winws / zapret | [bol-van/zapret](https://github.com/bol-van/zapret) | MIT; автор bol-van |
| WinDivert DLL и драйвер | [basil00/WinDivert](https://github.com/basil00/WinDivert), [исходные релизы](https://github.com/basil00/WinDivert/releases) | LGPLv3 либо GPLv2; полные тексты в `licenses/WinDivert-LICENSE.txt` |
| Cygwin DLL 3.4.10 | [Cygwin](https://cygwin.com/), [исходники версии](https://github.com/mirror/newlib-cygwin/tree/cygwin-3.4.10), [условия](https://cygwin.com/licensing.html) | LGPLv3+ с Cygwin Linking Exception; `licenses/Cygwin-LGPL.txt`, `licenses/GPL-3.0.txt` |
| .NET runtime | [dotnet/runtime](https://github.com/dotnet/runtime), [WPF](https://github.com/dotnet/wpf) | MIT и third-party notices в `licenses/` |
| Установщик | [Inno Setup](https://jrsoftware.org/isinfo.php) | Условия Inno Setup, отдельные от лицензии приложения |

Cygwin Linking Exception: copyright holders grant additional permission to link libcygwin.a, crt0.o and gcrt0.o with independent modules and convey the resulting executable under terms of your choice without LGPLv3 section 4 requirements; see the linked upstream licensing page for full terms.

Пакет компонентов фиксирован в `installer/prepare-components.ps1`: ZIP 1.10.2 и SHA-256 `5eaac9fb2e4b1abd693487452a3ff3f4dfe9578a45f9ddddfa4bc1f5a6bb62d5`. Скрипт проверяет архив перед извлечением. Исходники компонентов доступны по указанным ссылкам отдельно от исходников оболочки. Оболочка не ограничивает предусмотренные лицензиями права на изучение и изменение библиотек.
