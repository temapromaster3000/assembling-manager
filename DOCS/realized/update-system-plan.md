# План: установщик + обновления по кнопке из Revit

Статус: **realized** (реализовано и запушено 2026-09-02, коммиты fc4b714…d185053)

## Цель

Дать коллегам ставильщик (`AssemblingManager-<версия>-setup.exe`) с возможностью обновлять плагин кнопкой прямо из Revit, без повторного скачивания установщика с GitHub. Для собственной разработки ничего не меняется — `build-and-deploy.bat` продолжает работать как раньше.

Механизм повторяет подход плагина [AGK-SmartCon-Pro](https://github.com/Alexandrisius/AGK-SmartCon-Pro):
проверка GitHub Releases → скачивание ZIP во staging → маркер-файл → внешний апдейтер
дожидается закрытия Revit и подменяет DLL.

## Решения (согласованы с пользователем)

| Вопрос | Решение |
|---|---|
| Публикация релизов | Локальный скрипт `tools/release.bat` (gh CLI), без CI |
| Когда проверять обновления | Только по кнопке (без автопроверки при старте) |
| Формат установщика | Inno Setup (нужен только разработчику) |
| Кнопка на ленте | Панель «Настройки» первой на вкладке; кнопка открывает окно с версией и проверкой обновлений |
| Бета-канал | Пока только `main`; фильтр prerelease заложен в коде — бета добавится позже как prerelease-релизы |
| Стартовая версия | 1.0.0 (`Version.txt` — единственный источник версии) |

## Как это работает у коллег

1. Коллега скачивает `AssemblingManager-X.Y.Z-setup.exe` со страницы Releases и запускает.
   Установщик сам находит установленные Revit 2021–2025 (реестр + стандартные пути),
   кладёт DLL в `%APPDATA%\AssemblingManager\<Год>\` и прописывает `.addin`
   в `%APPDATA%\Autodesk\Revit\Addins\<Год>\`.
2. На ленте первая панель «Настройки» → окно с версией плагина и кнопкой
   «Проверить обновления».
3. Проверка: GitHub API `releases?per_page=20` репозитория
   `temapromaster3000/assembling-manager`; draft и prerelease пропускаются;
   выбирается наибольший стабильный тег `vX.Y.Z`.
4. Если есть новее: показываются release notes, кнопка «Скачать и установить».
   Скачивается ассет `AssemblingManager-R<YY>.zip` под версию запущенного Revit
   (заодно — ассеты для всех установленных годов плагина), распаковывается в
   `%APPDATA%\AssemblingManager\staging\<tag>\`, создаётся маркер
   `%APPDATA%\AssemblingManager\update-pending.txt`.
5. Запускается `AssemblingManager.Updater.exe` (консоль, net48): ждёт выхода всех
   процессов Revit (до 5 минут), копирует `*.dll`/`*.pdb` из staging в целевые
   папки годов, чистит staging, удаляет маркер. Лог —
   `%APPDATA%\AssemblingManager\updater-log.txt`.

Revit держит свои DLL заблокированными, поэтому подмена файлов невозможна изнутри
плагина — применяется только внешним процессом после выхода Revit.

## Этапы

| # | Этап | Файлы |
|---|---|---|
| 1 | Версионирование | `Version.txt`, `Directory.Build.props`, `Directory.Build.targets` |
| 2 | Окно «Настройки» + панель первой на ленте | `Views/SettingsDialog.xaml(.cs)`, `Commands/SettingsCommand.cs`, `App.cs` |
| 3 | Сервис обновлений | `Updates/UpdateService.cs`, пакет `System.Text.Json` |
| 4 | Апдейтер (консоль net48, без зависимостей) | `src/AssemblingManager.Updater/` |
| 5 | Установщик Inno Setup | `tools/installer/AssemblingManager-Setup.iss` |
| 6 | Скрипт релиза | `tools/release.ps1`, `tools/release.bat` |

Детали этапа 1: `Version.txt` читается в `Directory.Build.props` → свойство
`Version` (даёт `InformationalVersion` сборки). Проект `AssemblingManager.Updater`
исключается из валидации `RevitVersion` и всегда собирается как `net48`.

Детали этапа 3: маркер — простой текст (секции `[Artifact]` со `StagingDir` /
`TargetDir`), чтобы апдейтеру не нужны были JSON-библиотеки. Сравнение версий —
свой парсер трёх чисел в `Core/Utils/PluginVersion.cs`.

Детали этапа 5: установщик кладёт сборку каждого года отдельно
(2021/2022/2023 ← R21-билд, 2024 ← R24, 2025 ← R25), те же пути, что и у
`build-and-deploy.bat`, поэтому оба способа установки взаимозаменяемы.

Детали этапа 6: скрипт собирает Release.R21…R25, пакует
`artifacts/AssemblingManager-R21.zip`…`R25.zip`, собирает setup.exe через ISCC,
публикует релиз `gh release create vX.Y.Z` с notes из `tools/release-notes.md`.

## Коммиты

1. `chore: add Version.txt and version injection into builds`
2. `feat(updates): add settings window with update check on ribbon`
3. `feat(updates): add AssemblingManager.Updater console tool`
4. `feat(installer): add Inno Setup installer`
5. `feat(release): add release script`
6. `docs: move update-system plan to realized` (после пуша)

## Чек-лист ручной проверки (выполняет пользователь)

- [ ] Разово установлены Inno Setup 6 и GitHub CLI (`gh auth login`)
- [ ] Запуск `tools/release.bat` → на GitHub появились ZIP-ы R21–R25 и setup.exe
- [ ] Установка setup.exe на машину с Revit → вкладка «Assembling Manager» появилась, кнопка «Настройки» первая
- [ ] Окно «Настройки» показывает актуальную версию
- [ ] «Проверить обновления» при отсутствии новых → «У вас последняя версия»
- [ ] После публикации релиза с новой версией → обновление находится, скачивается; после закрытия Revit DLL-и обновлены (версия в окне изменилась)
- [ ] Обновление применяется ко всем установленным годам Revit, где стоит плагин
- [ ] Деинсталляция через Панель управления удаляет плагин и `.addin`
- [ ] `build-and-deploy.bat` после установки установщика продолжает работать
