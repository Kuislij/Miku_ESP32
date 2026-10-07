# MikuOS wire protocol v1

Кадр: `1|TYPE|ID|BASE64(UTF8(payload))\n`. Поток ASCII, LF delimiting, CRLF на входе допускается. Максимум кадра 8192 байта включая LF, максимум декодированного payload 4096 байт. Base64 обязателен даже для обычного текста; пустой payload — пустое четвёртое поле.

Пример запроса: `1|CMD|42|cGluZw==` + LF; ответ: `1|RES|42|cG9uZw==` + LF.

| TYPE | Payload | ID |
|---|---|---|
| CMD | Текст команды shell | Ненулевой uint32 |
| RES | Окончательный текстовый ответ | ID команды |
| ERR | Окончательная текстовая ошибка | ID команды |
| EVT | Имя события | 0 |
| LOG | Строка журнала | 0 |
| STAT | JSON статистики | 0 |
| TASKS | JSON массива задач | 0 |
| HB | alive | 0 |

Каждая принятая CMD получает ровно один RES/ERR, кроме физического разрыва или сбоя устройства. События, журнал и снимки могут идти до ответа. Reboot подтверждается и дожидается отправки UART перед restart. Пустая команда или неверные аргументы дают ERR. Повреждённому envelope без надёжного ID ответа нет. CMD с NUL получает ERR. Счётчик ID клиента обходит 0 и уже занятые ID при переполнении; pending ограничен 64, timeout 5 секунд.

Parser накапливает частичные кадры и обрабатывает несколько кадров в одном chunk. Он проверяет версию, числовой ID, canonical Base64 (включая padding), лимиты и UTF-8. При переполнении строка отбрасывается до LF. C++ вход принимает CMD, .NET вход принимает все перечисленные типы. ID=0 запрещён для CMD; ID=0 остальных типов — соглашение, не общая проверка parser. Firmware shell использует ASCII имена команд; UTF-8 может быть передан и отклонён как неизвестная команда.

STAT пример:

```json
{"model":"ESP32-S3 N16R8","firmware":"0.2.0","uptime":21,"freeHeap":363295,"minHeap":360975,"largestBlock":270336,"psramSize":8388608,"freePsram":8376636,"flashSize":16777216,"resetReason":"software","droppedMessages":0,"telemetryMs":1000,"taskCount":2,"load":null,"services":{"telemetry":true,"heartbeat":true,"demo":false}}
```

Размеры памяти — байты. freeHeap/minHeap/largestBlock относятся только к внутренней RAM с MALLOC_CAP_8BIT, PSRAM измеряется отдельно. uptime — целые секунды; taskCount — активные сервисы MikuOS, а не все FreeRTOS tasks. load=null, пока нет достоверного измерителя. services bool означает enabled и не Faulted; точное состояние находится в TASKS.

TASKS: `[{"id":1,"name":"telemetry","state":"Waiting","runs":10,"periodMs":1000}]`. Состояния Stopped, Ready, Running, Waiting, Faulted. ID определяется порядком регистрации, runs сохраняется при start/stop и сбрасывается при reboot.

События: system.connected, services.changed, system.rebooted (host/simulator), terminal.clear, media.ascii.open. После физического reboot платы приходит system.connected. Heartbeat раз в секунду, STAT с интервалом telemetryMs. Если оба сервиса остановлены, отсутствие фоновых кадров ожидаемо; клиент может запрашивать info. Native USB, TCP и WebSocket пока не реализованы. Локальный Serial не шифруется; сетевой доступ должен получить отдельную защиту.

Команды: help, ping, uname, version, uptime, mem, info, tasks, services, start/stop service, logs, config, config telemetry_ms 200..10000, clear, video, reboot, neofetch, miku. Конфигурация сохраняется в NVS на ESP32; simulator и host хранят её в памяти своего процесса/объекта.

## Файловые команды (firmware 0.3)

Внешний envelope остаётся v1. Файловые RPC используют `CMD`, `RES` с JSON и `ERR` с кодом. Пути — canonical Base64 от UTF-8, блоки — Base64 от произвольных байтов. Это внутренний Base64 в текстовом payload, который также кодируется внешним envelope. Клиент не печатает RPC-ответы в терминал; обычные команды продолжают выводиться.

| CMD payload | Ответ RES |
|---|---|
| `fs info` | ready, filesystem, total, free, maxFile, chunkSize, maxEntries, version |
| `fs list path64 cursor version` | version, entries[{name,directory,size}], next (null в конце) |
| `fs stat path64` / `fs hash path64` | size, directory, crc32, version |
| `fs read path64 offset length version` | offset, data, crc32, version |
| `fs begin path64 total crc32 expectedVersion` | token |
| `fs chunk token offset bytes64` | received (суммарное число принятых байтов) |
| `fs commit token` | version |
| `fs abort token` | {} |
| `fs mkdir path64` / `fs remove path64` | version |
| `fs rename old64 new64` | version |
| `fs copy old64 new64` | token (запускает фоновую задачу на ESP32) |
| `fs status token` | state (running/complete/failed), received, total, version, error |

Числа — десятичные неотрицательные значения uint32, CRC32 — IEEE (123456789 → CBF43926). Блок до 2048 байт; один файл до 4 МиБ. Первый list принимает `version=*`, следующие страницы — полученную версию. Read всегда требует точную версию stat. Begin принимает точную версию или `*` для явного сохранения без проверки; UI редактор использует точную версию. Отмена до commit сохраняет исходник. Если связь потеряна в момент commit, результат нужно перечитать, а не повторять запись автоматически.

Copy использует тот же staging и слот единственного upload, переносит один блок за проход worker и проверяет записанные байты чтением обратно. Client опрашивает status с интервалом 150 мс. Abort отменяет работающую копию; после complete существует целевой файл. Во время copy ручные chunk/commit для её token отклоняются. Сохраняется результат последней задачи copy до новой задачи или перезапуска. Закрытие клиента само по себе не останавливает копирование на плате.

EVT: `storage.changed` после завершённых изменений, `storage.upload.expired` после 30 секунд простоя, `storage.copy.completed` и `storage.copy.failed`. Долгий checksum передаёт `HB` с `storage busy`. Файловые RPC имеют клиентский timeout 60 секунд; обычные shell-запросы — 5 секунд. ERR включает INVALID_PATH, ROOT_PROTECTED, NOT_FOUND, NOT_A_FILE, CONFLICT, ALREADY_EXISTS, UPLOAD_BUSY, INVALID_TOKEN, OFFSET_MISMATCH, INVALID_CHUNK, CHECKSUM_MISMATCH, NO_SPACE и ошибки файловых операций.

Человекочитаемые shell-команды: `ls`, `cat`, `mkdir`, `rm`, `mv`, `cp`, `touch`, `write`, `df`. Ограничения и порядок восстановления описаны в [storage.md](storage.md).
