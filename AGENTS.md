# AGENTS.md — Assembling Manager (Revit plugin)

## Communication
- Отвечай пользователю на русском языке.
- Если требование, задача или ожидание пользователя неясны, неоднозначны или могут быть истолкованы по-разному — обязательно задай уточняющий вопрос через инструмент `question` прежде чем действовать. Лучше уточнить один раз, чем сделать не то.
- Пользователь новичок в программировании (знаком только с базовым Python) и в вайб-кодинге. Объясняй каждое действие простым языком, без лишнего сленга. Рассказывай, зачем делается шаг и что он даёт.

## Project identity
- C#-плагин для Autodesk Revit 2021–2025.
- Назначение: автоматическое создание видов (план, разрез, 3D) и спецификаций на основе сборок (`AssemblyInstance`) в модели.
- Целевые фреймворки: `net4.8` и `net8.0` (по необходимости `net6.0`/`net7.0` — уточнять в `.csproj`).

## Architecture
- Основная точка входа Revit — класс, реализующий `IExternalCommand` (или `IExternalApplication` для панели при запуске).
- Логика разделяется по слоям:
  - Команды / UI → `*Command.cs`.
  - Работа со сборками → `AssemblyService.cs`.
  - Создание видов → `ViewService.cs`.
  - Создание спецификаций → `ScheduleService.cs`.
- Все операции с моделью выполняются внутри `Transaction`/`TransactionGroup` с `TransactionMode.Manual`.

## Build & references
- Используй `msbuild` / `dotnet build`. При пустом репозитории — ещё нет `.sln`/`.csproj`; создавай их с мультицелевой сборкой под версии Revit.
- Ссылки на `RevitAPI.dll` и `RevitAPIUI.dll` — версионные; не коммить DLL в репозиторий.
- Рекомендуется `Directory.Build.props` с условными ссылками по `$(RevitVersion)` или `$(TargetFramework)`.
- Каждая версия плагина поставляется со своим `.addin` манифестом (имя/путь сборки должны совпадать).

## Developer workflow
- Build: пользователь собирает и развёртывает плагин самостоятельно через `build-and-deploy.bat` в корне репозитория. Агент НЕ должен запускать этот BAT-файл и не должен копировать файлы в `%APPDATA%\Autodesk\Revit\Addins\` без явного запроса.
- Для проверки компиляции агент может использовать `dotnet build` / `msbuild` с нужной конфигурацией (`Debug.R21`…`Debug.R25`).
- Debug: скопировать сборку и `.addin` в `%APPDATA%\Autodesk\Revit\Addins\<Year>\` и запускать Revit с отладчиком (Attach to process).
- Нет автоматических юнит-тестов без установленного Revit; предпочитай интеграционные проверки через загрузку плагина в Revit.

## API gotchas
- `IExternalCommand.Execute` требует `[Transaction(TransactionMode.Manual)]` и `[Regeneration(RegenerationOption.Manual)]`.
- Создание видов и спецификаций требует открытой транзакции; при ошибке делай `transaction.RollBack()`.
- Для 3D-вида используй `View3D.CreateIsometric`/`CreatePerspective` с правильным `ViewFamilyType`.
- Для спецификаций используй `ViewSchedule.CreateSchedule` и добавляй поля через `ScheduleDefinition.AddField`.
- Работа с `AssemblyInstance` требует проверки `IsValidObject` и активного документа.
