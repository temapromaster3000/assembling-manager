# Assembling Manager

C#-плагин для Autodesk Revit 2021–2025 для автоматического создания видов (план, разрез, 3D) на основе сборок (`AssemblyInstance`) в модели.

## Возможности

- Создание плана, двух разрезов и 3D-вида для каждой сборки в модели.
- Автоматическое определение видимых элементов через параметр `AssemblyParameter`.
- Рекурсивная обработка вложенных общих семейств.
- Скрытие посторонних элементов на виде с помощью фильтра по параметру.

## Структура проекта

```
assembling-manager
├── src/
│   ├── AssemblingManager.Core/      # Общие модели и константы
│   └── AssemblingManager.Revit/     # Точка входа Revit, команды, сервисы, UI
├── build/
│   └── AssemblingManager.sln        # Решение Visual Studio
├── Directory.Build.props            # Общие настройки сборки
├── Directory.Build.targets          # Версии NuGet-пакетов и выходные пути
├── addins/
│   ├── 2021/                        # Манифесты для каждой версии Revit
│   ├── 2022/
│   ├── 2023/
│   ├── 2024/
│   └── 2025/
├── build-and-deploy.bat             # Сборка и установка в Revit
└── README.md
```

## Требования

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) или новее.
- Установленный Revit 2021–2025 (для запуска и отладки).

## Сборка

Плагин использует NuGet-пакеты `Nice3point.Revit.Api.RevitAPI`, поэтому для сборки не требуется указывать путь к установленному Revit.

### Вручную

```powershell
dotnet build build\AssemblingManager.sln -c Debug.R21
```

Доступные конфигурации: `Debug.R21`, `Debug.R22`, `Debug.R23`, `Debug.R24`, `Debug.R25`.

Выходные файлы располагаются в:

```
bin\Debug.R21\2021\AssemblingManager.dll
bin\Debug.R25\2025\AssemblingManager.dll
```

### Автоматическая сборка и установка

Запустите в корне репозитория:

```cmd
build-and-deploy.bat
```

Скрипт:
1. Соберёт плагин под все версии Revit.
2. Скопирует DLL в `%APPDATA%\AssemblingManager\{Year}\`.
3. Создаст `.addin` манифест в `%APPDATA%\Autodesk\Revit\Addins\{Year}\`.

Если какая-то версия Revit не установлена, она будет пропущена.

## Отладка

1. Запустите `build-and-deploy.bat`.
2. Запустите Revit.
3. На вкладке **Assembling Manager** нажмите **Сформировать виды**.
4. В Visual Studio выберите **Debug → Attach to process** и подключитесь к `Revit.exe`.

## Важно

- `RevitAPI.dll` и `RevitAPIUI.dll` не включены в репозиторий — они приходят через NuGet.
- Для коммерческого использования замените `VendorId` и `VendorDescription` в `addins/20XX/AssemblingManager.addin`.
- `AddInId` в манифестах уже задан уникальным GUID. При конфликте с другим плагином замените его на новый.
