# MikuOS

Учебная мини-система ESP32 + Windows Forms Control Center. Первый рабочий вертикальный срез, версия 0.1.0. Плата не требуется: приложение автоматически подключается к Simulator.

## Запуск на этом компьютере

Откройте `Start-MikuOS.cmd` в корне проекта. Локальный .NET 8 SDK установлен в `.tools/dotnet` (не входит в исходники). Либо выполните:

```powershell
.tools/dotnet/dotnet.exe run --project control-center/MikuOS.ControlCenter.csproj -c Release
```

На другом компьютере нужен .NET 8 SDK для Windows. Используйте обычный `dotnet` вместо локального пути. Готовая сборка находится в `control-center/bin/Release/net8.0-windows`; для её запуска нужен .NET 8 Desktop Runtime x64.

## Что работает

- WinForms, тёмная тема, бирюзовые акценты; Dashboard, Terminal, Tasks, Services, Logs.
- История команд стрелками, timestamps, цветные ответы, автопрокрутка.
- Уptime, heap, график последних 120 измерений, RX/TX, обнаружение отсутствия данных и таймаутов ответов.
- Simulator и Serial 115200/8N1 через общий транспорт; строгий протокол с фрагментацией и восстановлением после повреждённых строк.
- Симулятор: команды `help`, `ping`, `info`, `tasks`, `services`, `start demo`, `stop demo`, `uptime`, `mem`, `logs`, `reboot`, `clear`, `version`, `uname`, `neofetch`, `miku`, `video`.
- C++ firmware: переносимое кооперативное ядро, состояния сервисов, очередь IPC на 32 сообщения, системный тик, shell, протокол; ESP-IDF UART и watchdog адаптер.
- C++ host/mock позволяет собирать и проверять логику firmware без ESP-IDF.

`video` уже открывает Media через событие протокола. Воспроизведение видео и ASCII-декодер **ещё не реализованы**: это следующий этап. System Monitor пока объясняет метрики; график находится на Dashboard. Анимации, постоянное хранение настроек, расширенные пользовательские задачи, GPIO/storage и кольцевой журнал firmware также остаются дальнейшими этапами. CPU load не выдумывается: передаётся `null`. Память симулятора — синтетическая.

## Проверка

```powershell
./scripts/build.ps1
.tools/dotnet/dotnet.exe control-center/bin/Release/net8.0-windows/MikuOS.ControlCenter.dll --smoke
cmake -S firmware -B firmware/build -DMIKU_HOST=ON
cmake --build firmware/build --config Release
ctest --test-dir firmware/build -C Release --output-on-failure
```

UI smoke запускает настоящее окно, проверяет обмен и навигацию, сохраняет `dashboard.png` и `smoke-result.txt` рядом со сборкой, затем закрывается. CMake здесь обнаружен в установленной Visual Studio; при отсутствии в PATH используйте Developer PowerShell.

## ESP32 позже

В окружении ESP-IDF 5.x: `cd firmware`, `idf.py set-target esp32`, `idf.py build`. Прошивка и реальные проверки выполняются только после появления платы. ESP-IDF сейчас не установлен; ESP32-адаптер не собран и аппаратно не проверен. UART0 использует стандартные пины целевой платы, нужен USB-UART; native USB для других моделей требует отдельной конфигурации.

См. `docs/architecture.md`, `docs/protocol.md`, `docs/hardware-testing.md`.
