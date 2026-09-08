# Убираем «Формирование графики» из сортировки листов и «Разместить на листах»

## Проблема

Пер-листовые коллекторы `FilteredElementCollector(doc, sheet.Id)` заставляют Revit вычислять
видимость в каждом листе — в строке состояния «Формирование графики», ~3 с на лист.
Оптимизация записей номеров (см. sheets-sort-report-and-performance.md) ускорила транзакцию
(4,74 с → 1,10 с), но общее время не изменилось: в анализе 49 листов × 2 коллектора ≈ 2,5 мин.
То же в «Разместить на листах» (`PrepareSheetsForPlacement` в цикле) — ~30 с.

## Решение

Однократный глобальный сбор + группировка по листам:

- `Dictionary<ElementId, List<Viewport>>` — все Viewport модели по `OwnerViewId`;
- `Dictionary<ElementId, List<ScheduleSheetInstance>>` — все ScheduleSheetInstance по `OwnerViewId`.

- `SortSheetsCommand.BuildAnalysis` и `CollectSignalSheetContent` читают из словарей.
- `SheetService.PrepareSheetsForPlacement` принимает словари (сбор один раз до цикла по листам
  в `PlaceViewsOnSheetsCommand`); при чтении проверка `doc.GetElement(...) != null` — вьюпорты
  могли быть удалены на предыдущих итерациях.
- `GetElementOnSheetCenter` / `MeasureItems` не трогаем — там габариты нужны по делу.
- Тайминги этапов сортировки в лог через `Logger.Time`.

## Файлы

| Файл | Изменение |
|---|---|
| `src/AssemblingManager.Revit/Commands/SortSheetsCommand.cs` | словари, сигнатуры, тайминги |
| `src/AssemblingManager.Revit/Services/SheetService.cs` | `PrepareSheetsForPlacement` из словарей |
| `src/AssemblingManager.Revit/Commands/PlaceViewsOnSheetsCommand.cs` | однократный сбор словарей |

## Контрольный список ручной проверки (в Revit)

- [ ] Сортировка: после «ОК» в строке состояния нет «Формирование графики» по листам;
      в логе BuildAnalysis < 1 с; общее время ≈ диалог + пара секунд.
- [ ] «Разместить на листах»: заметно быстрее; очистка дубликатов и раскладка не поехали.
- [ ] Сигнальные листы удаляются как раньше; отчёты без изменений.
- [ ] Сборка Debug.R21 / Debug.R25 без ошибок.
