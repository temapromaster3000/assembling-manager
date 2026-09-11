using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssignAssemblyPositionsCommand : IExternalCommand
    {
        private const string PositionParameterName = "ADSK_Позиция";
        private const string NameParameterName = "ADSK_Наименование";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            Document document = uiApplication.ActiveUIDocument?.Document;

            if (document == null)
            {
                TaskDialog.Show(Constants.PluginName, "Необходимо открыть модель.");
                return Result.Cancelled;
            }

            Logger.Info("=== AssignAssemblyPositionsCommand started ===");
            Logger.Info($"Document: {document.Title}");

            BrowserGroupService browserGroupService = new BrowserGroupService();
            List<ScheduleGroupNode> scheduleRoots;

            try
            {
                scheduleRoots = browserGroupService.BuildScheduleTree(document);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to build schedule tree: {ex}");
                TaskDialog.Show(Constants.PluginName, $"Не удалось прочитать структуру спецификаций: {ex.Message}");
                return Result.Failed;
            }

            int totalSchedules = scheduleRoots.Sum(r => r.CountSchedules());
            if (totalSchedules == 0)
            {
                Logger.Warn("No schedules found. Command cancelled.");
                TaskDialog.Show(Constants.PluginName, "В модели не найдено спецификаций для обработки.");
                return Result.Cancelled;
            }

            AssignPositionsDialog dialog = new AssignPositionsDialog(
                scheduleRoots,
                PositionPresetStorage.ReadPresetKeywords(document));
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true || dialog.SelectedSchedules == null || dialog.SelectedSchedules.Count == 0)
            {
                Logger.Info("User cancelled the positions dialog.");
                return Result.Cancelled;
            }

            List<ViewSchedule> selectedSchedules = dialog.SelectedSchedules;
            List<string> keywords = dialog.Keywords;
            bool onlyPositions = dialog.OnlyPositions;

            if (onlyPositions)
            {
                Logger.Info("Only-positions mode enabled: tags step will be skipped.");
            }

            PositionPresetStorage.SavePresetKeywords(document, keywords);

            ParameterService parameterService = new ParameterService();
            ElementId positionParameterId = parameterService.GetParameterByName(document, PositionParameterName);

            if (positionParameterId == null)
            {
                TaskDialog.Show(
                    Constants.PluginName,
                    $"Общий параметр «{PositionParameterName}» не найден в проекте. Сначала добавьте его и столбец в спецификации.");
                return Result.Cancelled;
            }

            ElementId nameParameterId = parameterService.GetParameterByName(document, NameParameterName);
            if (nameParameterId == null && keywords.Count > 0)
            {
                Logger.Warn($"Parameter '{NameParameterName}' not found. Keyword skipping is disabled.");
            }

            bool mergeSchedules = dialog.MergeSchedules && selectedSchedules.Count > 1;
            MergedNumberingState mergedState = null;

            if (mergeSchedules)
            {
                if (nameParameterId == null)
                {
                    Logger.Warn("Merge schedules requested but parameter 'ADSK_Наименование' not found; falling back to normal numbering.");
                    TaskDialog.Show(
                        Constants.PluginName,
                        "Объединение спецификаций недоступно: в модели не найден параметр «ADSK_Наименование».\n" +
                        "Нумерация будет выполнена отдельно по каждой спецификации.");
                    mergeSchedules = false;
                }
                else
                {
                    mergedState = new MergedNumberingState();
                    Logger.Info("Merged schedules numbering enabled.");
                }
            }

            Dictionary<ElementId, FamilySymbol> selectedTagSymbols = new Dictionary<ElementId, FamilySymbol>();
            int minOffsetMm = TagPresetStorage.DefaultMinOffsetMm;
            int zoneHeightMm = TagPresetStorage.DefaultZoneHeightMm;
            bool textBelowShelf = false;

            TagService tagService = new TagService();

            if (!onlyPositions)
            {
                List<Category> elementCategories = CollectElementCategories(document, selectedSchedules, tagService);
                Dictionary<ElementId, List<TagService.TagSymbolOption>> symbolOptions =
                    tagService.BuildTagSymbolOptions(document, elementCategories);

                if (elementCategories.Count > 0)
                {
                    if (symbolOptions.Count == 0)
                    {
                        Logger.Warn("No tag symbol options found for selected schedules.");
                        TaskDialog.Show(
                            Constants.PluginName,
                            "Для категорий элементов выбранных спецификаций не найдено подходящих марок в проекте.\n" +
                            "Позиции будут записаны, марки — пропущены.\n\n" +
                            "Проверьте лог плагина: там видны категории элементов и найденные марки.");
                    }

                    List<TagCategoryItem> dialogCategories = BuildTagCategoryItems(elementCategories, symbolOptions);
                    IReadOnlyDictionary<string, string> preset = TagPresetStorage.ReadPreset(document);
                    int presetMinOffsetMm = TagPresetStorage.ReadMinOffsetMm(document);
                    int presetZoneHeightMm = TagPresetStorage.ReadZoneHeightMm(document);
                    bool presetTextBelowShelf = TagPresetStorage.ReadTextBelowShelf(document);

                    AssignTagsDialog tagsDialog = new AssignTagsDialog(
                        dialogCategories,
                        preset,
                        presetMinOffsetMm,
                        presetZoneHeightMm,
                        presetTextBelowShelf);
                    bool? tagsDialogResult = tagsDialog.ShowDialog();

                    if (tagsDialogResult != true)
                    {
                        Logger.Info("User cancelled the tags dialog.");
                        return Result.Cancelled;
                    }

                    selectedTagSymbols = new Dictionary<ElementId, FamilySymbol>(
                        tagsDialog.SelectedSymbolsByCategoryId);
                    minOffsetMm = tagsDialog.MinOffsetMm;
                    zoneHeightMm = tagsDialog.ZoneHeightMm;
                    textBelowShelf = tagsDialog.TextBelowShelf;

                    TagPresetStorage.SavePreset(
                        document,
                        BuildTagPreset(dialogCategories, tagsDialog.SelectedSymbolsByCategoryId),
                        minOffsetMm,
                        zoneHeightMm,
                        textBelowShelf);

                    Logger.Info($"Selected tag symbols: {selectedTagSymbols.Count} categories.");
                    Logger.Info($"Min offset to element: {minOffsetMm} mm.");
                    Logger.Info($"Zone height: {zoneHeightMm} mm, text below shelf: {textBelowShelf}.");
                }
                else
                {
                    Logger.Warn("No element categories found for selected schedules; tags step skipped.");
                }
            }
            else
            {
                Logger.Info("Only-positions mode: tags dialog skipped, existing tags left untouched.");
            }

            Dictionary<ElementId, TagService.TagSymbolOption> tagChoices = null;

            if (selectedTagSymbols.Count > 0)
            {
                tagChoices = BuildTagSymbolChoices(selectedTagSymbols);
                tagService.PrepareTagDimensions(document, selectedSchedules, tagChoices);
            }

            Logger.Info($"Selected schedules: {selectedSchedules.Count}. Keywords: {keywords.Count}.");

            Stopwatch stopwatch = Stopwatch.StartNew();
            int processedCount = 0;
            int skippedCount = 0;
            TagPlacementResult tagResult = null;
            List<string> headerWarnings = new List<string>();

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    using (Transaction transaction = new Transaction(document, "Проставить позиции"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        foreach (ViewSchedule schedule in selectedSchedules)
                        {
                            bool handled = ProcessSchedule(
                                document,
                                schedule,
                                positionParameterId,
                                nameParameterId,
                                keywords,
                                mergedState);

                            if (handled)
                            {
                                processedCount++;

                                try
                                {
                                    schedule.RefreshData();
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn($"Could not refresh schedule '{schedule.Name}': {ex.Message}");
                                }
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }

                        if (mergedState != null)
                        {
                            ScheduleService scheduleService = new ScheduleService();
                            scheduleService.ApplyMergedScheduleHeaders(selectedSchedules, headerWarnings);

                            Logger.Info(
                                $"Merged numbering finished: last position {mergedState.LastPosition}, " +
                                $"reused {mergedState.ReusedCount}, new {mergedState.NewCount}.");
                        }

                        document.Regenerate();
                        Logger.Info("Document regenerated to refresh schedule views.");

                        if (tagChoices != null && tagChoices.Count > 0)
                        {
                            tagResult = tagService.PlaceTags(
                                document,
                                selectedSchedules,
                                tagChoices,
                                minOffsetMm,
                                zoneHeightMm,
                                textBelowShelf);

                            Logger.Info(
                                $"Tags placed: created {tagResult.TagsCreatedCount}, deleted {tagResult.TagsDeletedCount}, " +
                                $"skipped {tagResult.ElementsSkippedCount}, views {tagResult.TaggedViewsCount}, " +
                                $"warnings {tagResult.Warnings.Count}.");
                        }

                        transaction.Commit();
                        Logger.Info("Transaction committed.");
                    }

                    transactionGroup.Assimilate();
                    Logger.Info("TransactionGroup assimilated.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception during execution: {ex}");
                    transactionGroup.RollBack();
                    Logger.Info("TransactionGroup rolled back.");
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            stopwatch.Stop();

            Logger.Info($"=== AssignAssemblyPositionsCommand finished in {stopwatch.Elapsed.TotalSeconds:F2} s: processed {processedCount}, skipped {skippedCount} ===");

            TaskDialog report = new TaskDialog(Constants.PluginName)
            {
                MainInstruction = $"Обработано спецификаций: {processedCount}",
                MainContent = $"Пропущено спецификаций: {skippedCount}\n" +
                              $"Время работы: {stopwatch.Elapsed.TotalSeconds:F1} с" +
                              (mergeSchedules ? "\nОбъединение спецификаций: включено" : string.Empty),
                CommonButtons = TaskDialogCommonButtons.Ok
            };

            if (headerWarnings.Count > 0)
            {
                report.MainContent += "\n\nЗаголовки спецификаций:\n" + string.Join("\n", headerWarnings.Select(w => $"— {w}"));
            }

            if (onlyPositions)
            {
                report.MainContent += "\n\nМарки: пропущены (режим «только позиции»)";
            }
            else if (tagResult != null)
            {
                report.MainContent +=
                    $"\n\nМарки:\nСоздано марок: {tagResult.TagsCreatedCount}\n" +
                    $"Удалено предыдущих: {tagResult.TagsDeletedCount}\n" +
                    $"Пропущено элементов: {tagResult.ElementsSkippedCount}\n" +
                    $"Пропущено (смотрит в нас): {tagResult.ElementsCutSkippedCount}\n" +
                    $"Видов с марками: {tagResult.TaggedViewsCount}";

                foreach (string warning in tagResult.Warnings)
                {
                    report.MainContent += $"\n— {warning}";
                }
            }

            report.Show();

            return Result.Succeeded;
        }

        private static bool ProcessSchedule(
            Document doc,
            ViewSchedule schedule,
            ElementId positionParameterId,
            ElementId nameParameterId,
            List<string> keywords,
            MergedNumberingState mergedState)
        {
            string scheduleName = schedule.Name;

            if (schedule == null || !schedule.IsValidObject)
            {
                Logger.Warn($"Schedule '{scheduleName}' is not valid. Skipping.");
                return false;
            }

            ScheduleDefinition definition = schedule.Definition;

            if (FindField(definition, positionParameterId) == null)
            {
                Logger.Warn($"Schedule '{scheduleName}' does not contain parameter '{PositionParameterName}'. Skipping.");
                return false;
            }

            List<ScheduleField> groupingFields = GetGroupingFields(definition, positionParameterId);
            if (groupingFields.Count == 0)
            {
                Logger.Warn($"Schedule '{scheduleName}' has no grouping fields (all fields are non-parameter). Skipping.");
                return false;
            }

            List<Element> elements = new FilteredElementCollector(doc, schedule.Id)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => !(e is Material))
                .ToList();

            if (elements.Count == 0)
            {
                Logger.Warn($"Schedule '{scheduleName}' has no elements. Skipping.");
                return false;
            }

            if (elements.Count > 0)
            {
                Element first = elements[0];
                Logger.Info($"Schedule '{scheduleName}' first element: Id {first.Id}, category '{first.Category?.Name}'.");
                Parameter positionParameter = first.LookupParameter(PositionParameterName);
                if (positionParameter == null)
                {
                    Logger.Info($"  '{PositionParameterName}' NOT FOUND via LookupParameter on first element.");
                }
                else
                {
                    Logger.Info(
                        $"  '{PositionParameterName}' found: definition='{positionParameter.Definition?.Name}', " +
                        $"storage={positionParameter.StorageType}, readOnly={positionParameter.IsReadOnly}, shared={positionParameter.IsShared}");
                }

                foreach (Parameter p in first.Parameters)
                {
                    Logger.Debug($"  parameter '{p.Definition?.Name}' = '{p.AsString()}'");
                }
            }

            IList<ScheduleSortGroupField> sortGroupFields = definition.GetSortGroupFields();
            bool hasSortFields = sortGroupFields != null && sortGroupFields.Count > 0;

            Dictionary<ScheduleFieldId, ScheduleSortOrder> sortDirections = new Dictionary<ScheduleFieldId, ScheduleSortOrder>();

            if (hasSortFields)
            {
                foreach (ScheduleSortGroupField sortGroupField in sortGroupFields)
                {
                    sortDirections[sortGroupField.FieldId] = sortGroupField.SortOrder;
                }
            }

            Dictionary<GroupKey, List<Element>> groups = new Dictionary<GroupKey, List<Element>>();
            Dictionary<GroupKey, List<SortValue>> sortValuesByGroup = new Dictionary<GroupKey, List<SortValue>>();

            foreach (Element element in elements)
            {
                List<SortValue> sortValues = GetFieldSortValues(doc, element, groupingFields);
                GroupKey key = new GroupKey(sortValues.Select(v => v.Text).ToList());
                List<Element> groupElements;

                if (!groups.TryGetValue(key, out groupElements))
                {
                    groupElements = new List<Element>();
                    groups[key] = groupElements;
                    sortValuesByGroup[key] = sortValues;
                }

                groupElements.Add(element);
            }

            List<string> rawFieldSummary = new List<string>();

            foreach (ScheduleSortGroupField sortGroupField in sortGroupFields)
            {
                ScheduleField rawField = definition.GetField(sortGroupField.FieldId);
                string rawName = rawField != null ? rawField.GetName() : sortGroupField.FieldId.ToString();
                rawFieldSummary.Add($"'{rawName}' ({sortGroupField.SortOrder})");
            }

            Logger.Info($"Schedule '{scheduleName}' raw sort fields: {string.Join(", ", rawFieldSummary)}.");

            List<string> fieldSummary = new List<string>();

            foreach (ScheduleField field in groupingFields)
            {
                ScheduleSortOrder order;
                sortDirections.TryGetValue(field.FieldId, out order);
                string direction = order == ScheduleSortOrder.Descending ? "desc" : "asc";
                fieldSummary.Add($"'{field.GetName()}' ({direction})");
            }

            Logger.Info($"Schedule '{scheduleName}' grouping fields: {string.Join(", ", fieldSummary)}.");

            List<KeyValuePair<GroupKey, List<Element>>> orderedGroups = groups.ToList();

            if (hasSortFields)
            {
                orderedGroups.Sort(new GroupComparer(groupingFields, sortDirections, sortValuesByGroup));
            }
            else
            {
                Logger.Info($"Schedule '{scheduleName}': no sort/group fields — groups keep model order.");
            }

            for (int i = 0; i < orderedGroups.Count; i++)
            {
                Logger.Debug($"Group order {i + 1}: [{string.Join(" | ", orderedGroups[i].Key.Values)}].");
            }

            int position = 0;
            int skippedGroups = 0;
            int totalSet = 0;
            int totalMissing = 0;
            int reusedCount = 0;
            int newCount = 0;
            bool readbackLogged = false;
            Dictionary<string, int> missingByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<GroupKey, List<Element>> pair in orderedGroups)
            {
                if (keywords.Count > 0
                    && nameParameterId != null
                    && pair.Value.Any(e => ContainsKeyword(e, nameParameterId, keywords)))
                {
                    skippedGroups++;
                    continue;
                }

                string identityName = mergedState != null ? GetGroupIdentityName(pair.Value, nameParameterId) : null;
                int assignedPosition;

                if (mergedState != null
                    && identityName != null
                    && mergedState.TryGetPosition(identityName, out int existingPosition))
                {
                    assignedPosition = existingPosition;
                    reusedCount++;
                    mergedState.CountReused();
                }
                else
                {
                    if (mergedState != null)
                    {
                        assignedPosition = mergedState.NextPosition();
                    }
                    else
                    {
                        position++;
                        assignedPosition = position;
                    }

                    if (mergedState != null && identityName != null)
                    {
                        mergedState.Register(identityName, assignedPosition);
                    }

                    newCount++;
                }

                Logger.Debug(
                    $"Position {assignedPosition}: [{string.Join(" | ", pair.Key.Values)}] ({pair.Value.Count} element(s)).");

                bool groupSet = false;

                foreach (Element element in pair.Value)
                {
                    Parameter positionParameter = element.LookupParameter(PositionParameterName);
                    if (positionParameter == null)
                    {
                        totalMissing++;

                        string categoryName = element.Category != null ? element.Category.Name : "?";
                        int count;
                        missingByCategory.TryGetValue(categoryName, out count);
                        missingByCategory[categoryName] = count + 1;

                        if (assignedPosition == 1 && !groupSet)
                        {
                            Logger.Info($"Element {element.Id} (category '{categoryName}') has no parameter '{PositionParameterName}'.");
                        }

                        continue;
                    }

                    try
                    {
                        bool ok;

                        if (positionParameter.StorageType == StorageType.String)
                        {
                            ok = positionParameter.Set(assignedPosition.ToString());
                        }
                        else
                        {
                            ok = positionParameter.Set(assignedPosition);
                        }

                        totalSet++;
                        groupSet = true;

                        if (!ok)
                        {
                            Logger.Warn($"Element {element.Id}: parameter.Set returned FALSE for position {assignedPosition}.");
                        }

                        if (!readbackLogged)
                        {
                            readbackLogged = true;
                            string readback = null;

                            try
                            {
                                readback = positionParameter.StorageType == StorageType.String
                                    ? positionParameter.AsString()
                                    : positionParameter.AsInteger().ToString();
                            }
                            catch
                            {
                            }

                            Logger.Info(
                                $"First write: element {element.Id}, category '{element.Category?.Name}', set={assignedPosition}, readback='{readback}', " +
                                $"definition='{positionParameter.Definition?.Name}', storage={positionParameter.StorageType}, readOnly={positionParameter.IsReadOnly}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not set position {assignedPosition} on element {element.Id}: {ex.Message}");
                    }
                }

                if (!groupSet)
                {
                    Logger.Warn($"Group at position {assignedPosition} could not be written (no element has parameter '{PositionParameterName}').");
                }
            }

            Logger.Info(
                mergedState != null
                    ? $"Schedule '{scheduleName}': merged numbering (new {newCount}, reused {reusedCount}, set {totalSet}, missing {totalMissing}, {skippedGroups} groups skipped by keywords, {groups.Count} groups total)."
                    : $"Schedule '{scheduleName}': positions {position} (set {totalSet}, missing {totalMissing}, {skippedGroups} groups skipped by keywords, {groups.Count} groups total).");

            if (missingByCategory.Count > 0)
            {
                foreach (KeyValuePair<string, int> pair in missingByCategory)
                {
                    Logger.Info($"  missing '{PositionParameterName}': category '{pair.Key}' — {pair.Value} elements.");
                }
            }

            return true;
        }

        private static bool ContainsKeyword(Element element, ElementId nameParameterId, List<string> keywords)
        {
            Parameter nameParameter = element.LookupParameter(NameParameterName);
            string elementName = nameParameter != null ? nameParameter.AsString() : null;

            if (string.IsNullOrEmpty(elementName))
            {
                return false;
            }

            return keywords.Any(k => elementName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetGroupIdentityName(List<Element> groupElements, ElementId nameParameterId)
        {
            if (nameParameterId == null || nameParameterId == ElementId.InvalidElementId)
            {
                return null;
            }

            foreach (Element element in groupElements)
            {
                Parameter nameParameter = element.LookupParameter(NameParameterName);
                string value = nameParameter != null && nameParameter.HasValue ? nameParameter.AsString() : null;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private class MergedNumberingState
        {
            private readonly Dictionary<string, int> _positionsByName =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public int LastPosition { get; private set; }
            public int ReusedCount { get; private set; }
            public int NewCount { get; private set; }

            public bool TryGetPosition(string identityName, out int position)
            {
                return _positionsByName.TryGetValue(identityName, out position);
            }

            public int NextPosition()
            {
                LastPosition++;
                return LastPosition;
            }

            public void Register(string identityName, int position)
            {
                _positionsByName[identityName] = position;

                if (position > LastPosition)
                {
                    LastPosition = position;
                }

                NewCount++;
            }

            public void CountReused()
            {
                ReusedCount++;
            }
        }

        private static List<ScheduleField> GetGroupingFields(ScheduleDefinition definition, ElementId positionParameterId)
        {
            List<ScheduleField> result = new List<ScheduleField>();

            IList<ScheduleSortGroupField> sortGroupFields = definition.GetSortGroupFields();
            if (sortGroupFields.Count > 0)
            {
                foreach (ScheduleSortGroupField sortGroupField in sortGroupFields)
                {
                    ScheduleField field = definition.GetField(sortGroupField.FieldId);
                    if (field != null
                        && field.ParameterId != ElementId.InvalidElementId
                        && field.ParameterId != positionParameterId)
                    {
                        result.Add(field);
                    }
                }

                if (result.Count == 0)
                {
                    result = CollectAllParameterFields(definition, positionParameterId);
                }
            }
            else
            {
                result = CollectAllParameterFields(definition, positionParameterId);
            }

            return result;
        }

        private static List<ScheduleField> CollectAllParameterFields(ScheduleDefinition definition, ElementId positionParameterId)
        {
            List<ScheduleField> result = new List<ScheduleField>();

            foreach (ScheduleFieldId fieldId in definition.GetFieldOrder())
            {
                ScheduleField field = definition.GetField(fieldId);
                if (field != null
                    && field.ParameterId != ElementId.InvalidElementId
                    && field.ParameterId != positionParameterId)
                {
                    result.Add(field);
                }
            }

            return result;
        }

        private static List<SortValue> GetFieldSortValues(Document doc, Element element, List<ScheduleField> fields)
        {
            List<SortValue> values = new List<SortValue>(fields.Count);

            foreach (ScheduleField field in fields)
            {
                Parameter parameter = FindParameterById(element, field.ParameterId);

                if (parameter == null || !parameter.HasValue)
                {
                    Element typeElement = element.GetTypeId() != ElementId.InvalidElementId
                        ? doc.GetElement(element.GetTypeId())
                        : null;

                    parameter = typeElement != null ? FindParameterById(typeElement, field.ParameterId) : null;
                }

                if (parameter == null || !parameter.HasValue)
                {
                    values.Add(new SortValue { Text = string.Empty });
                    continue;
                }

                switch (parameter.StorageType)
                {
                    case StorageType.Double:
                        values.Add(new SortValue
                        {
                            Number = parameter.AsDouble(),
                            IsNumeric = true,
                            Text = parameter.AsString() ?? string.Empty
                        });
                        break;
                    case StorageType.Integer:
                        values.Add(new SortValue
                        {
                            Number = parameter.AsInteger(),
                            IsNumeric = true,
                            Text = parameter.AsString() ?? string.Empty
                        });
                        break;
                    default:
                        values.Add(new SortValue { Text = parameter.AsString() ?? string.Empty });
                        break;
                }
            }

            return values;
        }

        private static Parameter FindParameterById(Element element, ElementId parameterId)
        {
            if (parameterId == null || parameterId == ElementId.InvalidElementId)
            {
                return null;
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.Id == parameterId)
                {
                    return parameter;
                }
            }

            return null;
        }

        private class GroupKey : IEquatable<GroupKey>
        {
            public List<string> Values { get; }

            public GroupKey(List<string> values)
            {
                Values = values;
            }

            public bool Equals(GroupKey other)
            {
                return other != null && Values.SequenceEqual(other.Values, StringComparer.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as GroupKey);
            }

            public override int GetHashCode()
            {
                int hash = 17;
                foreach (string value in Values)
                {
                    hash = hash * 31 + (value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value));
                }

                return hash;
            }
        }

        private class SortValue
        {
            public double? Number { get; set; }
            public bool IsNumeric { get; set; }
            public string Text { get; set; }
        }

        private class GroupComparer : IComparer<KeyValuePair<GroupKey, List<Element>>>
        {
            private static readonly NaturalStringComparer Natural = new NaturalStringComparer();
            private readonly List<ScheduleField> _fields;
            private readonly Dictionary<ScheduleFieldId, ScheduleSortOrder> _directions;
            private readonly Dictionary<GroupKey, List<SortValue>> _sortValues;

            public GroupComparer(
                List<ScheduleField> fields,
                Dictionary<ScheduleFieldId, ScheduleSortOrder> directions,
                Dictionary<GroupKey, List<SortValue>> sortValues)
            {
                _fields = fields;
                _directions = directions;
                _sortValues = sortValues;
            }

            public int Compare(KeyValuePair<GroupKey, List<Element>> x, KeyValuePair<GroupKey, List<Element>> y)
            {
                List<SortValue> valuesX;
                _sortValues.TryGetValue(x.Key, out valuesX);
                List<SortValue> valuesY;
                _sortValues.TryGetValue(y.Key, out valuesY);

                for (int i = 0; i < _fields.Count; i++)
                {
                    SortValue valueX = GetAt(valuesX, i);
                    SortValue valueY = GetAt(valuesY, i);

                    int cmp;
                    if (valueX != null && valueY != null
                        && valueX.IsNumeric && valueY.IsNumeric
                        && valueX.Number.HasValue && valueY.Number.HasValue)
                    {
                        cmp = valueX.Number.Value.CompareTo(valueY.Number.Value);
                    }
                    else
                    {
                        string textX = valueX != null ? valueX.Text ?? string.Empty : string.Empty;
                        string textY = valueY != null ? valueY.Text ?? string.Empty : string.Empty;
                        cmp = Natural.Compare(textX, textY);
                    }

                    if (cmp != 0)
                    {
                        ScheduleSortOrder order;
                        if (_directions.TryGetValue(_fields[i].FieldId, out order)
                            && order == ScheduleSortOrder.Descending)
                        {
                            return -cmp;
                        }

                        return cmp;
                    }
                }

                return 0;
            }

            private static SortValue GetAt(List<SortValue> values, int index)
            {
                return values != null && index < values.Count ? values[index] : null;
            }
        }

        private static ScheduleField FindField(ScheduleDefinition definition, ElementId parameterId)
        {
            foreach (ScheduleFieldId fieldId in definition.GetFieldOrder())
            {
                ScheduleField field = definition.GetField(fieldId);
                if (field != null && field.ParameterId == parameterId)
                {
                    return field;
                }
            }

            return null;
        }

        private static List<Category> CollectElementCategories(
            Document doc,
            IEnumerable<ViewSchedule> schedules,
            TagService tagService)
        {
            return tagService.GetElementCategories(doc, schedules);
        }

        private static List<TagCategoryItem> BuildTagCategoryItems(
            IReadOnlyList<Category> categories,
            IReadOnlyDictionary<ElementId, List<TagService.TagSymbolOption>> options)
        {
            List<TagCategoryItem> items = new List<TagCategoryItem>();

            foreach (Category category in categories)
            {
                List<TagService.TagSymbolOption> symbolOptions;
                options.TryGetValue(category.Id, out symbolOptions);

                List<TagOptionItem> optionItems = (symbolOptions ?? new List<TagService.TagSymbolOption>())
                    .Select(o => new TagOptionItem(o.DisplayName, o.Symbol))
                    .ToList();

                items.Add(new TagCategoryItem(category, optionItems));
            }

            return items;
        }

        private static Dictionary<ElementId, TagService.TagSymbolOption> BuildTagSymbolChoices(
            IReadOnlyDictionary<ElementId, FamilySymbol> selectedSymbols)
        {
            Dictionary<ElementId, TagService.TagSymbolOption> result =
                new Dictionary<ElementId, TagService.TagSymbolOption>();

            foreach (KeyValuePair<ElementId, FamilySymbol> pair in selectedSymbols)
            {
                if (pair.Value == null || pair.Value.Family == null)
                {
                    continue;
                }

                bool isMulti = TagService.IsMultiCategoryTag(pair.Value);

                result[pair.Key] = new TagService.TagSymbolOption(pair.Value, isMulti);
            }

            return result;
        }

        private static Dictionary<string, string> BuildTagPreset(
            IEnumerable<TagCategoryItem> categories,
            IReadOnlyDictionary<ElementId, FamilySymbol> selectedSymbols)
        {
            Dictionary<string, string> preset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (TagCategoryItem item in categories)
            {
                FamilySymbol symbol;
                if (selectedSymbols.TryGetValue(item.Category.Id, out symbol))
                {
                    preset[item.CategoryName] = symbol.Family.Name + TagPresetStorage.EntrySeparator + symbol.Name;
                }
            }

            return preset;
        }
    }
}
