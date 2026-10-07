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
