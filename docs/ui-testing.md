# Control Center — проверка 7 октября 2026

## Control Center 0.4 / Firmware 0.3

94 .NET проверки прошли, включая асинхронные RPC, файл с двоичными данными, многостраничный каталог, конфликт версии, отмену upload/copy, reconnect, reboot, timeout и disconnect. C++ kernel/storage unit tests и host-интеграция проходят отдельно.

Готовая автономная Windows x64 сборка автоматически находит COM5. Проверены настоящий файловый менеджер и редактор: отдельная тестовая папка, чтение UTF-8, правки в RichTextBox, запись на ESP32, повторное чтение, copy/rename и удаление своей папки. Smoke сверяет полные байты с BOM и CRLF. Переподключение не заменяет несохранённый буфер редактора.

Также повторно проверены заставка, реальные STAT/TASKS, команды и service state, обычное видео, ASCII и отмена/закрытие FFmpeg. Скриншот страницы «Файлы» — [screenshots/files.png](screenshots/files.png). Он показывает настоящий FATFS-том платы. Начальные файлы читаются через USB, локальный demo-том не используется.

Результат полной проверки:

```text
PASS: Auto COM5, boot animation, telemetry, tasks, commands, service state,
media event, files/editor/copy/rename, rendering,
video frame rendering, ASCII decoding and cancellation
```

Ниже сохранён отчёт предыдущей версии.

## Control Center 0.3

Проверена автономная Windows x64 сборка `artifacts/ControlCenter/MikuOS.ControlCenter.exe`, созданная `scripts/publish.ps1`. Firmware на плате остаётся 0.2.0.

- 76 .NET checks: протокол, симулятор, сессия, ASCII, новый поиск устройств.
- Поиск пропускает занятые и посторонние порты; требует коррелированный pong и корректный снимок ESP32/MikuOS; освобождает порт после каждой пробы.
- Отсутствие портов, отсутствие ответов, неподходящий JSON, неверный ID ответа и отмена проверены на тестовых транспортах.
- При запуске готового `.exe` с `--smoke --auto` плата автоматически обнаружена на COM5. Порт в аргументах не указан.
- В заставке сравниваются два кадра: проверяется изменение пикселей персонажа, работа таймера и завершение заставки.
- Реальные STAT/TASKS, ping, start/stop demo, события media, отрисовка главного экрана и всех остальных страниц.
- Локальный тестовый ролик: обычное видео, ASCII в терминале, отмена декодера и закрытие во время воспроизведения. После теста процессы FFmpeg не остались.
- Ярлык `F:\ESP32\MikuOS.lnk` указывает на готовый `.exe`, передаёт `--auto` и использует папку сборки как рабочую.

Результат готовой сборки:

```text
PASS: Auto COM5, boot animation, telemetry, tasks, commands, service state,
media event, rendering, video frame rendering, ASCII decoding and cancellation
```

Артефакты проверки сохраняются рядом с `.exe`: `smoke-result.txt`, `dashboard.png`, `boot-animation.png`, `boot-animation-next.png`, `ascii-terminal.png`, `video-preview.png`, `page-2.png` … `page-7.png`.

Физическое отключение/подключение кабеля в этой UI-проверке не выполнялось. Логика повторного поиска реализована; таймауты и освобождение транспорта проверены отдельно. Проверка звука на аудиодорожке и других масштабов DPI не проводилась. Заставка показывается в Windows-приложении: для изображения на самой ESP32 нужен подключённый дисплей.
