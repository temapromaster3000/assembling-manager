using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Common;
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

            Logger.Info($"Selected schedules: {selectedSchedules.Count}. Keywords: {keywords.Count}.");

            Stopwatch stopwatch = Stopwatch.StartNew();
            int processedCount = 0;
            int skippedCount = 0;

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
                            bool handled = ProcessSchedule(document, schedule, positionParameterId, nameParameterId, keywords);

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

                        document.Regenerate();
                        Logger.Info("Document regenerated to refresh schedule views.");

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
                              $"Время работы: {stopwatch.Elapsed.TotalSeconds:F1} с",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            report.Show();

            return Result.Succeeded;
        }

        private static bool ProcessSchedule(
            Document doc,
            ViewSchedule schedule,
            ElementId positionParameterId,
            ElementId nameParameterId,
            List<string> keywords)
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

            Dictionary<GroupKey, List<Element>> groups = new Dictionary<GroupKey, List<Element>>();

            foreach (Element element in elements)
            {
                GroupKey key = new GroupKey(GetFieldValues(doc, element, groupingFields));
                List<Element> groupElements;

                if (!groups.TryGetValue(key, out groupElements))
                {
                    groupElements = new List<Element>();
                    groups[key] = groupElements;
                }

                groupElements.Add(element);
            }

            List<KeyValuePair<GroupKey, List<Element>>> orderedGroups = groups.ToList();
            orderedGroups.Sort(new GroupComparer(groupingFields, definition.GetSortGroupFields()));

            int position = 0;
            int skippedGroups = 0;
            int totalSet = 0;
            int totalMissing = 0;
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

                position++;
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

                        if (position == 1 && !groupSet)
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
                            ok = positionParameter.Set(position.ToString());
                        }
                        else
                        {
                            ok = positionParameter.Set(position);
                        }

                        totalSet++;
                        groupSet = true;

                        if (!ok)
                        {
                            Logger.Warn($"Element {element.Id}: parameter.Set returned FALSE for position {position}.");
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
                                $"First write: element {element.Id}, category '{element.Category?.Name}', set={position}, readback='{readback}', " +
                                $"definition='{positionParameter.Definition?.Name}', storage={positionParameter.StorageType}, readOnly={positionParameter.IsReadOnly}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not set position {position} on element {element.Id}: {ex.Message}");
                    }
                }

                if (!groupSet)
                {
                    Logger.Warn($"Group at position {position} could not be written (no element has parameter '{PositionParameterName}').");
                }
            }

            Logger.Info(
                $"Schedule '{scheduleName}': positions {position} (set {totalSet}, missing {totalMissing}, {skippedGroups} groups skipped by keywords, {groups.Count} groups total).");

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

        private static List<string> GetFieldValues(Document doc, Element element, List<ScheduleField> fields)
        {
            List<string> values = new List<string>(fields.Count);

            foreach (ScheduleField field in fields)
            {
                string parameterName = field.GetName();
                Parameter parameter = element.LookupParameter(parameterName);

                if (parameter == null || !parameter.HasValue)
                {
                    Element typeElement = element.GetTypeId() != ElementId.InvalidElementId
                        ? doc.GetElement(element.GetTypeId())
                        : null;

                    parameter = typeElement != null ? typeElement.LookupParameter(parameterName) : null;
                }

                string value = parameter != null && parameter.HasValue ? parameter.AsString() : string.Empty;
                values.Add(value ?? string.Empty);
            }

            return values;
        }

        private static int FindColumnByParamId(TableSectionData body, ElementId parameterId)
        {
            for (int column = body.FirstColumnNumber; column <= body.LastColumnNumber; column++)
            {
                ElementId cellParamId = body.GetCellParamId(body.FirstRowNumber, column);
                if (cellParamId == parameterId)
                {
                    return column;
                }
            }

            return -1;
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

        private class GroupComparer : IComparer<KeyValuePair<GroupKey, List<Element>>>
        {
            private readonly List<ScheduleField> _fields;
            private readonly Dictionary<ScheduleFieldId, ScheduleSortOrder> _directions;

            public GroupComparer(List<ScheduleField> fields, IList<ScheduleSortGroupField> sortGroupFields)
            {
                _fields = fields;
                _directions = new Dictionary<ScheduleFieldId, ScheduleSortOrder>();

                foreach (ScheduleSortGroupField sortGroupField in sortGroupFields)
                {
                    _directions[sortGroupField.FieldId] = sortGroupField.SortOrder;
                }
            }

            public int Compare(KeyValuePair<GroupKey, List<Element>> x, KeyValuePair<GroupKey, List<Element>> y)
            {
                for (int i = 0; i < _fields.Count; i++)
                {
                    ScheduleField field = _fields[i];
                    string valueX = i < x.Key.Values.Count ? x.Key.Values[i] ?? string.Empty : string.Empty;
                    string valueY = i < y.Key.Values.Count ? y.Key.Values[i] ?? string.Empty : string.Empty;

                    int cmp = string.Compare(valueX, valueY, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0)
                    {
                        ScheduleSortOrder order;
                        if (_directions.TryGetValue(field.FieldId, out order)
                            && order == ScheduleSortOrder.Descending)
                        {
                            return -cmp;
                        }

                        return cmp;
                    }
                }

                return 0;
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
    }
}
