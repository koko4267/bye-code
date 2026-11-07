# bye-code

Утилита для полного удаления Visual Studio Code и всех связанных данных из Windows.

## Удаляемые компоненты

- `%APPDATA%\Code` и `%APPDATA%\Code - Insiders` (настройки, профили, кэш, сессии)
- `%USERPROFILE%\.vscode`, `%USERPROFILE%\.vscode-insiders`, `%USERPROFILE%\.vscode-cli` (расширения, бинарники)
- `%LOCALAPPDATA%\Programs\Microsoft VS Code` и `%ProgramFiles%\Microsoft VS Code`
- `%LOCALAPPDATA%\Code-updater`, `%LOCALAPPDATA%\CrashDumps`
- `%TEMP%\vscode-*`, логи обновлений Inno Setup
- Ярлыки (Рабочий стол, Меню «Пуск», Taskbar) и Jump Lists
- Контекстные меню («Открыть с помощью Code»), ассоциации файлов и протоколы `vscode://`
- Учетные данные Settings Sync и аккаунтов в Credential Manager
- Записи VS Code в системной и пользовательской переменной `PATH`

## Использование

Запуск без параметров открывает интерактивное меню:
```cmd
ByeCode.exe
```

Параметры командной строки:
```text
ByeCode.exe [параметры]
```

- `-s, --scan` — сканирование без удаления (dry-run)
- `-c, --clean` — полное удаление
- `-f, --force` — пропуск подтверждения
- `--no-insiders` — исключить VS Code Insiders
- `-h, --help` — справка

Примеры:
```cmd
ByeCode.exe --scan
ByeCode.exe --clean --force
```

## Сборка

Требуется .NET SDK 10+:
```cmd
dotnet publish src/ByeCode.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o bin
```
