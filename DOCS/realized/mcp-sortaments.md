# MCP-управление сортаментами труб

Статус: **реализовано** 26.11.2026. Основные кейсы и упаковка проверены в Revit 2025.
Telegram реализован отдельным планом: `docs/realized/telegram-integration-plan.md`.

## Итог реализации

- `SortamentService.PreviewChange` — общий план и проверки для preview/apply.
- `SortamentChangePlan`: изменения add/update/remove, полный предлагаемый каталог,
  число труб, число правил Routing Preferences, хэш каталога, блокеры worksharing.
- `McpPreviewStore`: одноразовый `previewId`, срок жизни 10 минут, привязка к документу,
  единицам и хэшу каталога.
- `preview_pipe_size_change` и `apply_pipe_size_change` в мосте и MCP-сервере.
- Apply: `TransactionGroup` + `Transaction`, rollback, один Ctrl+Z; ошибки
  `WRITE_DISABLED`, `READ_ONLY_DOCUMENT`, `PREVIEW_EXPIRED`, `CATALOG_CHANGED`,
  `WORKSHARING_BUSY`.
- `opencode.json` и `opencode.mcp.example.json`: правило `revit_apply_pipe_size_change` → `ask`.
- Исправлено падение окна «Сортаменты»: `InnerDiameter`/`NominalDiameter` → `Mode=OneWay`.
- Режим работы: предпросмотр технически обязателен, но чат применяет изменение сразу
  после просьбы пользователя; подтверждение — запрос разрешения opencode.
- Упаковка: `build-and-deploy.bat` собирает MCP-сервер в `bin\Debug\McpServer` и
  копирует в `%APPDATA%\AssemblingManager\McpServer`; `release.ps1` публикует его
  в `artifacts\publish\McpServer`, пакует `AssemblingManager-McpServer.zip` и
  передаёт инсталлятору.
- Проверены: add/update/remove, отказ при удалении используемого DN, Ctrl+Z,
  `WRITE_DISABLED`, `PREVIEW_EXPIRED`, `CATALOG_CHANGED`, `TIMEOUT`,
  полный цикл окна «Сортаменты», read-only и отсутствие документа.
- Worksharing на проверочной модели не применим (модель не в файл-хранилище);
  отдельная workshared-модель не проверялась.
- Отдельный future-план: `docs/future/sortaments-schedule-type-cleanup.md`.
- Telegram-интеграция: `docs/realized/telegram-integration-plan.md`.

## Цель

Дать возможность управлять сортаментами труб из чата (opencode, при желании — Telegram) через
фиксированный набор MCP-инструментов, без генерации и загрузки мини-плагинов на каждый запрос.

Первая версия:

- просмотр каталогов `PipeSegment` и их размеров;
- добавление нового размера;
- изменение наружного диаметра и толщины стенки у существующего DN;
- удаление неиспользуемого размера;
- один сегмент за вызов;
- обязательный предпросмотр → подтверждение → применение;
- запись по умолчанию выключена.

Остальные модули плагина (виды, листы, позиции, оси, сортаменты) подключаются следующими этапами.

## Архитектура

```text
Чат (opencode / Telegram)
   │
   ▼
MCP-сервер (отдельный exe, net8)
   │  именованный канал + токен, только этот компьютер
   ▼
Мост внутри AssemblingManager.dll (процесс Revit)
   │  ExternalEvent → главный поток Revit
   ▼
Prod services → TransactionGroup → Revit API
```

- MCP-сервер запускает клиент (opencode) по stdio.
- Мост живёт внутри Revit, слушает локальный именованный канал.
- Запросы к Revit API выполняются только в главном потоке через `ExternalEvent`.
- Запись — в `TransactionGroup` + `Transaction`: один вызов = один Ctrl+Z.
- Динамический C#-код мост не принимает.

## Инструменты

### Этап 0–1 (read-only)

| Инструмент | Доступ | Назначение |
|---|---|---|
| `status` | чтение | Связь, версия Revit, документ, единицы, включена ли запись, путь к логу |
| `list_instances` | чтение | Запущенные Revit/мосты |
| `list_pipe_segments` | чтение | Каталоги: имя, материал, спецификация, число труб |
| `get_pipe_segment_sizes` | чтение | Размеры сегмента: DN, наружный, стенка, внутренний, флаги, число труб |

### Этап 2–3 (write)

| Инструмент | Доступ | Назначение |
|---|---|---|
| `preview_pipe_size_change` | чтение | План изменения + одноразовый `previewId` |
| `apply_pipe_size_change` | запись | Применить ровно тот план по `previewId` |

Операции: `add`, `update`, `remove`. DN — ключ и не меняется. Удалять DN, используемый
трубами, нельзя; менять наружный диаметр/стенку можно — трубы сохраняют DN и переезжают
в новый сегмент.

## Текущее состояние (25.11.2026)

- Этапы 0–3 реализованы и проверены на Revit 2025; основные кейсы записи пройдены.
- `revit_status` видит документ, активный вид и единицы; `revit_list_instances` видит инстанс.
- `revit_list_pipe_segments` и `revit_get_pipe_segment_sizes` возвращают реальные данные.
- Проверочная модель: `Проект1 2025`, единицы — миллиметры; запись включается в настройках.
- На момент проверки: сегмент `570457` «Сталь углеродистая - Электросварная» (11 размеров,
  DN65 = 100 × 3.5, используется 4 трубами); сегмент `570429` «Сталь, нержавеющая AISI 304…»
  (DN65 = 150 × 3.5, используется 4 трубами). Id сегментов меняются при каждой замене.
- MCP-сервер запущен из `src\AssemblingManager.McpServer\bin\Debug\net8.0` и держит файлы.
  Пересборка выполнена через переименование занятых файлов в `.old`; два `.old` файла
  удалятся после перезапуска opencode.
- `opencode.json` локальный и в `.gitignore`; шаблон — `opencode.mcp.example.json`.
  Используется формат opencode V2 (`mcp.servers`), правило `apply` → `ask`.
- **25.11.2026, побочная находка:** окно «Сортаменты» падало при открытии
  (`TwoWay`-привязка к доступному только для чтения `InnerDiameter`), после чего Revit
  падал на показе `TaskDialog`. Исправлено: у `NominalDiameter` и `InnerDiameter`
  привязка переведена в `Mode=OneWay`. Открытие окна проверено, полный цикл редактирования
  из окна — ещё нет.
- Журнал Revit предупреждает: плагин собран против Revit API **2025.4.60**, установлен
  Revit **2025.4.50**. Это следствие wildcard `2025.*` в `Directory.Build.targets`;
  отдельное решение — закрепить версию пакета по минимальной поддерживаемой ревизии.
- **25.11.2026, побочная находка:** при каждой замене сегмента Revit копирует
  `PipeScheduleType`, и в модели копятся неиспользуемые типы «Электросварная 1, 2, 3».
  Сам сегмент переименовывается правильно. План правки вынесен в
  `docs/future/sortaments-schedule-type-cleanup.md`, чинить после MCP-тестов.

## Этапы

### Этап 0. Каркас

- [x] План зафиксирован.
- [x] `McpBridgeService`: именованный канал, токен, discovery-файл, `status`.
- [x] `McpEventHandler`: очередь + `ExternalEvent`.
- [x] `McpSettings`: `%APPDATA%\AssemblingManager\mcp-settings.json`.
- [x] Секция MCP в окне «Настройки».
- [x] MCP-сервер `AssemblingManager.McpServer` (net8), инструменты `status`, `list_instances`.
- [x] `--selftest` в MCP-сервере.
- [x] Сборка `Debug.R25` и `Debug.R21`.
- [x] Пользователь проверил связь в Revit 2025.

### Этап 1. Чтение сортаментов

- [x] `UsedPipeCount` в `SortamentSizeRow`.
- [x] `list_pipe_segments`.
- [x] `get_pipe_segment_sizes`.
- [x] Проверка чтения на модели «Проект1 2025».

### Этап 2. Предпросмотр

- [x] `SortamentService.PreviewChange` с общими проверками для preview/apply.
- [x] Подсчёт труб и routing preferences.
- [x] Проверка занятости в файл-хранилище (worksharing blockers + checkout в apply).
- [x] `preview_pipe_size_change` + `PreviewStore` с одноразовым `previewId`.

### Этап 3. Запись

- [x] `apply_pipe_size_change`.
- [x] Транзакция + rollback, один Ctrl+Z.
- [x] Выключатель записи в настройках.
- [x] Permissions в opencode: `apply` → `ask`.
- [x] Ошибки: `WRITE_DISABLED`, `WORKSHARING_BUSY`, `PREVIEW_EXPIRED`, `CATALOG_CHANGED`
  (дополнительно `READ_ONLY_DOCUMENT`).

### Этап 4. Интеграционная проверка

- [x] Добавление размера (DN90, 102 × 4.0).
- [x] Изменение OD/стенки используемого DN (DN65: 76 → 100 и 76 → 150).
- [x] Удаление неиспользуемого DN (DN90).
- [x] Отказ при удалении используемого DN (DN65, 4 трубы).
- [x] Ctrl+Z откатывает всё одним шагом.
- [x] Запись выключена → preview работает, apply даёт `WRITE_DISABLED`.
- [x] Модальное окно → `TIMEOUT`.
- [x] Изменение каталога между preview и apply → `CATALOG_CHANGED`
  (проверено: preview A, затем apply B, затем apply A).
- [x] Файл-хранилище: занятые элементы — на модели `Проект1 2025` не применимо
  (не workshared); отдельная workshared-модель не проверялась.
- [x] Нет документа → `NO_DOCUMENT` (подтверждено пользователем).
- [x] Документ только для чтения → `READ_ONLY_DOCUMENT` (подтверждено пользователем).
- [x] Revit закрыт → `BRIDGE_NOT_FOUND` (подтверждено пользователем).

### Этап 5. Документация и упаковка

- [x] README: раздел «Управление из чата (MCP)».
- [x] `opencode.mcp.example.json`.
- [x] Сборка/публикация MCP-сервера в `build-and-deploy.bat` / release:
  dev-сборка кладётся в `%APPDATA%\AssemblingManager\McpServer`, релиз — в
  `artifacts\publish\McpServer` и отдельный `AssemblingManager-McpServer.zip`,
  инсталлятор ставит сервер в `{app}\McpServer`. Команды сборки, publish и
  robocopy проверены; полный прогон `build-and-deploy.bat` — за пользователем.
- [x] Перемещение плана в `docs/realized` после пуша.

### Этап 6. Telegram (вынесено в отдельный план)

- [x] План и реализация: `docs/realized/telegram-integration-plan.md`.
- [x] Реализовано и проверено 26.11.2026.

## Безопасность

- Именованный канал только на этом компьютере.
- Токен на сессию Revit, в логах маскируется.
- Мост по умолчанию выключен; запись по умолчанию выключена.
- `apply` требует подтверждения в opencode (permissions) и включённого выключателя записи.
- Никакого динамического C#-кода.
- Telegram: только whitelist, long polling, opencode server на `127.0.0.1`.

## Ручная проверка (Revit 2025)

- [x] Собрать `Debug.R21`–`Debug.R25` и MCP-сервер.
- [x] Развернуть плагин, перезапустить Revit.
- [x] Открыть модель с трубами («Проект1 2025»).
- [x] В настройках плагина включить мост; запись включалась/выключалась по ходу проверок.
- [x] `revit_status` видит документ и единицы.
- [x] `revit_list_instances` видит инстанс.
- [x] `revit_list_pipe_segments` возвращает каталоги.
- [x] `revit_get_pipe_segment_sizes` возвращает размеры.
- [x] `preview_pipe_size_change` (update DN65, наружный 76 → 100) показывает план и `previewId`.
- [x] `apply_pipe_size_change` применяет план; 4 трубы DN65 переезжают в новый сегмент.
- [x] `preview_pipe_size_change` (update DN65 у нержавеющей стали, 76 → 150) и apply.
- [x] `preview_pipe_size_change` (add DN90, 102 × 4.0) и apply добавляют размер.
- [x] `preview_pipe_size_change` (remove DN90) и apply удаляют размер.
- [x] remove DN65 отклоняется: размер используется 4 трубами.
- [x] Повторный apply с тем же `previewId` даёт `PREVIEW_EXPIRED`.
- [x] Изменение каталога между preview и apply даёт `CATALOG_CHANGED`.
- [x] Запись выключена → preview работает, apply даёт `WRITE_DISABLED`.
- [x] Ctrl+Z откатывает изменение одним шагом: DN90 исчез, DN65 = 100 остался.
- [x] Модальное окно → вызовы дают `TIMEOUT` (проверено на открытых настройках).
- [x] Окно «Сортаменты» открывается без ошибки после `Mode=OneWay` (11:52).
- [x] Полный цикл редактирования из окна «Сортаменты» (изменить размер → применить).
- [x] Документ только для чтения → `READ_ONLY_DOCUMENT` (подтверждено пользователем).
- [x] Нет открытого документа → `NO_DOCUMENT` (подтверждено пользователем).
- [x] Worksharing: на модели `Проект1 2025` не применимо — она не в файл-хранилище.

Диагностика без opencode:

```powershell
src\AssemblingManager.McpServer\bin\Debug\net8.0\AssemblingManager.McpServer.exe --selftest
```

Лог плагина: `%APPDATA%\Autodesk\Revit\Addins\AssemblingManager\Logs\assembling-manager-*.log`
(искать строки `[MCP]`).

MCP-сервер по-прежнему не входит в `build\AssemblingManager.sln` (у проекта нет `Build.0`),
поэтому `build-and-deploy.bat` собирает его отдельным шагом в `bin\Debug\McpServer` и
копирует в `%APPDATA%\AssemblingManager\McpServer` через `robocopy`. Если сервер запущен
из этой папки, `robocopy` вернёт ошибку блокировки и скрипт выведет предупреждение:
закройте opencode и повторите. Релизный скрипт публикует сервер в
`artifacts\publish\McpServer`, пакует `AssemblingManager-McpServer.zip` и передаёт папку
инсталлятору (`{app}\McpServer`).

## Открытые решения

- Транспорт: именованный канал Windows. Запасной вариант при проблемах с правами — TCP 127.0.0.1 + токен.
- Один сегмент за вызов. Пакетный режим — позже.
- Предпросмотр обязателен технически для всех изменений: он выполняет проверки и создаёт
  одноразовый `previewId`. Показ плана и отдельное подтверждение в чате — по желанию
  пользователя; по умолчанию чат применяет изменение сразу, подтверждение — запрос
  разрешения opencode (решение 25.11.2026).
- Telegram — отдельный этап после стабильного ядра.
