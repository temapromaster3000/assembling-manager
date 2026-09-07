using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    public class SortSheetsCommand : IExternalCommand
    {
        private static readonly NaturalStringComparer KeyComparer = new NaturalStringComparer();
        private readonly SheetService _sheetService = new SheetService();
        private readonly WorksharingService _worksharingService = new WorksharingService();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            Document document = uiApplication.ActiveUIDocument?.Document;

            if (document == null)
            {
                TaskDialog.Show(Constants.PluginName, "Необходимо открыть модель.");
                return Result.Cancelled;
            }

            Logger.Info("=== SortSheetsCommand started ===");
            Logger.Info($"Document: {document.Title}");

            BrowserGroupService browserGroupService = new BrowserGroupService();
            List<SheetGroupNode> sheetRoots;

            try
            {
                sheetRoots = browserGroupService.BuildSheetTree(document);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to build sheet tree: {ex}");
                TaskDialog.Show(Constants.PluginName, $"Не удалось прочитать структуру диспетчера листов: {ex.Message}");
                return Result.Failed;
            }

            int totalSheets = sheetRoots.Sum(r => r.CountSheets());
            if (totalSheets == 0)
            {
                Logger.Warn("No sheets found. Command cancelled.");
                TaskDialog.Show(Constants.PluginName, "В модели нет листов.");
                return Result.Cancelled;
            }

            SheetsSortDialog dialog = new SheetsSortDialog(document, sheetRoots, _sheetService);
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true || dialog.SelectedGroupNode == null)
            {
                Logger.Info("User cancelled the sheets-sort dialog.");
                return Result.Cancelled;
            }

            SheetGroupNode selectedGroup = dialog.SelectedGroupNode;
            int startNumber = dialog.StartNumber;
            bool deleteSignalSheets = dialog.DeleteSignalSheets;

            if (deleteSignalSheets)
            {
                Logger.Info("Delete-signal-sheets mode enabled.");
            }

            List<SheetAnalysis> analyses = BuildAnalysis(document, selectedGroup, _sheetService.GetAssemblyNames(document));
            Logger.Info($"Group '{selectedGroup.Name}': {analyses.Count} sheets, {analyses.Count(a => a.IsEmpty)} empty, {analyses.Count(a => a.IsSignal)} signal.");

            List<SheetAnalysis> signalSheets = deleteSignalSheets
                ? analyses.Where(a => a.IsSignal && !a.IsEmpty).ToList()
                : new List<SheetAnalysis>();

            List<SheetAnalysis> remaining = analyses
                .Where(a => !a.IsEmpty && !(deleteSignalSheets && a.IsSignal))
                .ToList();
            List<SheetAnalysis> ordered = OrderSheets(remaining);

            if (analyses.All(a => a.IsEmpty))
            {
                Logger.Info("All sheets in group are empty.");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            SheetsSortResult result = new SheetsSortResult();
            List<string> renumberLog = new List<string>();
            List<string> renameLog = new List<string>();
            List<string> warnings = new List<string>();

            List<ElementId> emptySheetIds = analyses.Where(a => a.IsEmpty).Select(a => a.Sheet.Id).ToList();

            List<List<ElementId>> signalDeletionGroups = new List<List<ElementId>>();
            int preservedForeignCount = 0;
            if (signalSheets.Count > 0)
            {
                HashSet<string> assemblyNames = _sheetService.GetAssemblyNames(document);
                foreach (SheetAnalysis signal in signalSheets)
                {
                    List<ElementId> group = CollectSignalSheetContent(document, signal.Sheet, assemblyNames, out int foreignCount);
                    preservedForeignCount += foreignCount;
                    signalDeletionGroups.Add(group);
                }

                Logger.Info($"Signal sheets to delete: {signalDeletionGroups.Count}, assembly views/schedules to delete: {signalDeletionGroups.Sum(g => g.Count - 1)}.");
            }

            List<ElementId> deleteCandidateIds = emptySheetIds
                .Concat(signalDeletionGroups.SelectMany(g => g))
                .Distinct()
                .ToList();
            List<ElementId> modifySheetIds = ordered.Select(a => a.Sheet.Id).ToList();

            WorksharingEditability editability = _worksharingService.CheckEditable(
                document,
                deleteCandidateIds.Concat(modifySheetIds).Distinct().ToList());
            HashSet<ElementId> editableIds = editability.EditableIds;

            Dictionary<ElementId, string> ownersById = new Dictionary<ElementId, string>();
            foreach (WorksharingSkippedElement skipped in editability.SkippedElements)
            {
                ownersById[skipped.Id] = skipped.Owner;
            }

            List<ElementId> deletableIds = new List<ElementId>();
            HashSet<ElementId> deletableSignalSheetIds = new HashSet<ElementId>();
            HashSet<ElementId> deletableSignalViewIds = new HashSet<ElementId>();
            int skippedEmptyCount = 0;
            int skippedSignalSheetCount = 0;
            int skippedSignalViewCount = 0;

            foreach (ElementId emptyId in emptySheetIds)
            {
                if (editableIds.Contains(emptyId))
                {
                    deletableIds.Add(emptyId);
                }
                else
                {
                    skippedEmptyCount++;
                }
            }

            foreach (List<ElementId> group in signalDeletionGroups)
            {
                if (group.All(editableIds.Contains))
                {
                    deletableIds.AddRange(group);
                    deletableSignalSheetIds.Add(group[0]);
                    for (int i = 1; i < group.Count; i++)
                    {
                        deletableSignalViewIds.Add(group[i]);
                    }
                }
                else
                {
                    skippedSignalSheetCount++;
                    skippedSignalViewCount += group.Count - 1;
                }
            }

            if (skippedEmptyCount > 0 || skippedSignalSheetCount > 0 || skippedSignalViewCount > 0)
            {
                warnings.Add($"Не удалены (заняты другими пользователями): сигнальных листов — {skippedSignalSheetCount}, пустых листов — {skippedEmptyCount}, видов и спецификаций — {skippedSignalViewCount}.");
            }

            if (preservedForeignCount > 0)
            {
                warnings.Add($"Посторонние виды и спецификации, сохранённые в проекте: {preservedForeignCount}.");
            }

            HashSet<ElementId> nonEditableSheetIds = new HashSet<ElementId>(
                modifySheetIds.Where(id => !editableIds.Contains(id)));

            if (nonEditableSheetIds.Count > 0)
            {
                warnings.Add($"Не переименовано/перенумеровано (занято другими пользователями): {nonEditableSheetIds.Count} листов.");
            }

            foreach (WorksharingSkippedElement skipped in editability.SkippedElements)
            {
                Element element = document.GetElement(skipped.Id);
                string elementDescription = element != null ? element.Name : skipped.Id.ToString();
                Logger.Info($"Skipped (owner '{skipped.Owner}', {skipped.Reason}): {elementDescription} ({skipped.Id}).");
            }

            if (editability.SkippedElements.Count > 0)
            {
                IEnumerable<string> ownerGroups = editability.SkippedElements
                    .GroupBy(s => s.Owner)
                    .Select(g => $"{g.Key} ({g.Count()})");
                warnings.Add("Владельцы занятых элементов: " + string.Join(", ", ownerGroups) + ".");
            }

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    using (Transaction transaction = new Transaction(document, "Сортировка листов"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        if (deletableIds.Count > 0)
                        {
                            document.Delete(deletableIds);

                            int survivedSignalSheets = 0;
                            int survivedSignalViews = 0;
                            int survivedEmpty = 0;
                            foreach (ElementId elementId in deletableIds)
                            {
                                if (document.GetElement(elementId) != null)
                                {
                                    if (deletableSignalSheetIds.Contains(elementId))
                                    {
                                        survivedSignalSheets++;
                                    }
                                    else if (deletableSignalViewIds.Contains(elementId))
                                    {
                                        survivedSignalViews++;
                                    }
                                    else
                                    {
                                        survivedEmpty++;
                                    }
                                }
                            }

                            result.DeletedCount = deletableIds.Count
                                - deletableSignalSheetIds.Count
                                - deletableSignalViewIds.Count
                                - survivedEmpty;
                            result.SignalSheetsDeletedCount = deletableSignalSheetIds.Count - survivedSignalSheets;
                            result.SignalViewsDeletedCount = deletableSignalViewIds.Count - survivedSignalViews;

                            Logger.Info(
                                $"Deleted {result.DeletedCount} empty sheets, " +
                                $"{result.SignalSheetsDeletedCount} signal sheets, " +
                                $"{result.SignalViewsDeletedCount} assembly views/schedules from signal sheets.");

                            if (survivedSignalSheets + survivedSignalViews + survivedEmpty > 0)
                            {
                                warnings.Add($"Часть удалений не закрепилась (элементы заняты другими пользователями): листов — {survivedSignalSheets + survivedEmpty}, видов и спецификаций — {survivedSignalViews}.");
                                Logger.Warn($"Survived after deletion: {survivedSignalSheets + survivedEmpty} sheets, {survivedSignalViews} views/schedules.");
                            }
                        }

                        HashSet<string> occupiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (SheetAnalysis analysis in ordered)
                        {
                            occupiedNames.Add(SheetService.GetSheetName(analysis.Sheet).Trim());
                        }

                        HashSet<string> validAssemblyNames = _sheetService.GetAssemblyNames(document);

                        foreach (SheetAnalysis analysis in ordered)
                        {
                            if (nonEditableSheetIds.Contains(analysis.Sheet.Id))
                            {
                                continue;
                            }

                            string sheetName = SheetService.GetSheetName(analysis.Sheet).Trim();
                            if (analysis.SingleBaseName != null
                                && validAssemblyNames.Contains(analysis.SingleBaseName)
                                && sheetName != analysis.SingleBaseName)
                            {
                                try
                                {
                                    if (RenameSheet(analysis.Sheet, analysis.SingleBaseName, occupiedNames, renameLog))
                                    {
                                        result.RenamedCount++;
                                        Logger.Info($"Renamed sheet '{sheetName}' to '{analysis.SingleBaseName}'.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Could not rename sheet '{sheetName}' to '{analysis.SingleBaseName}': {ex}");
                                    warnings.Add($"Не удалось переименовать лист «{sheetName}»: {ex.Message}");
                                }
                            }
                        }

                        RenumberSheets(document, ordered, nonEditableSheetIds, ownersById, startNumber, renumberLog, warnings);
                        result.RenumberedCount = renumberLog.Count;

                        document.Regenerate();
                        Logger.Info("Document regenerated to refresh the project browser.");

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

            RefreshProjectBrowser(uiApplication);

            stopwatch.Stop();

            Logger.Info($"=== SortSheetsCommand finished in {stopwatch.Elapsed.TotalSeconds:F2} s: renamed {result.RenamedCount}, deleted {result.DeletedCount} empty sheets, signal sheets {result.SignalSheetsDeletedCount}, signal views/schedules {result.SignalViewsDeletedCount}, renumbered {result.RenumberedCount} ===");

            SheetsSortReportDialog reportDialog = new SheetsSortReportDialog(
                result.RenamedCount,
                result.DeletedCount,
                result.SignalSheetsDeletedCount,
                result.SignalViewsDeletedCount,
                result.RenumberedCount,
                renameLog,
                warnings);
            reportDialog.ShowDialog();

            return Result.Succeeded;
        }

        private static void RefreshProjectBrowser(UIApplication uiApplication)
        {
            try
            {
                DockablePane projectBrowser = uiApplication.GetDockablePane(
                    DockablePanes.BuiltInDockablePanes.ProjectBrowser);

                projectBrowser.Hide();
                projectBrowser.Show();

                Logger.Info("Project Browser refreshed.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not refresh Project Browser: {ex.Message}");
            }
        }

        private List<SheetAnalysis> BuildAnalysis(Document doc, SheetGroupNode group, HashSet<string> assemblyNames)
        {
            List<SheetAnalysis> result = new List<SheetAnalysis>();
            HashSet<ElementId> sheetsWithContent = _sheetService.GetSheetIdsWithMeaningfulContent(doc, assemblyNames);

            foreach (ViewSheet sheet in group.GetAllSheets())
            {
                SheetAnalysis analysis = new SheetAnalysis
                {
                    Sheet = sheet,
                    IsEmpty = _sheetService.IsSheetEmpty(sheet, sheetsWithContent),
                    IsSignal = SheetService.IsSignalSheetName(SheetService.GetSheetName(sheet)),
                    BaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    OldNumber = sheet.SheetNumber?.Trim() ?? string.Empty
                };

                foreach (Viewport viewport in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                {
                    View view = doc.GetElement(viewport.ViewId) as View;
                    if (view == null)
                    {
                        continue;
                    }

                    string baseName = SheetService.ParseBaseName(view.Name);
                    if (IsAssemblyBaseName(baseName, view.Name, assemblyNames))
                    {
                        analysis.BaseNames.Add(baseName);
                    }
                }

                foreach (ScheduleSheetInstance instance in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                {
                    ViewSchedule schedule = doc.GetElement(instance.ScheduleId) as ViewSchedule;
                    if (schedule == null)
                    {
                        continue;
                    }

                    string baseName = SheetService.ParseBaseName(schedule.Name);
                    if (IsAssemblyBaseName(baseName, schedule.Name, assemblyNames))
                    {
                        analysis.BaseNames.Add(baseName);
                    }
                }

                result.Add(analysis);
            }

            return result;
        }

        private List<ElementId> CollectSignalSheetContent(
            Document doc,
            ViewSheet sheet,
            HashSet<string> assemblyNames,
            out int foreignCount)
        {
            string sheetName = SheetService.GetSheetName(sheet);
            foreignCount = 0;
            List<ElementId> ids = new List<ElementId> { sheet.Id };

            foreach (Viewport viewport in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                View view = doc.GetElement(viewport.ViewId) as View;
                if (view == null)
                {
                    continue;
                }

                string baseName = SheetService.ParseBaseName(view.Name);
                if (IsAssemblyBaseName(baseName, view.Name, assemblyNames))
                {
                    ids.Add(view.Id);
                }
                else
                {
                    foreignCount++;
                }
            }

            foreach (ScheduleSheetInstance instance in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                if (instance.IsTitleblockRevisionSchedule)
                {
                    continue;
                }

                ViewSchedule schedule = doc.GetElement(instance.ScheduleId) as ViewSchedule;
                if (schedule == null)
                {
                    continue;
                }

                string scheduleName = schedule.Name ?? string.Empty;
                bool isAssemblySchedule = scheduleName.EndsWith(ScheduleService.ScheduleSuffix, StringComparison.Ordinal)
                    || assemblyNames.Contains(SheetService.ParseBaseName(scheduleName));

                if (isAssemblySchedule)
                {
                    ids.Add(schedule.Id);
                }
                else
                {
                    foreignCount++;
                }
            }

            Logger.Info($"Signal sheet '{sheetName}': {ids.Count - 1} assembly views/schedules queued for deletion, {foreignCount} foreign items kept in the project.");
            return ids;
        }

        private List<SheetAnalysis> OrderSheets(List<SheetAnalysis> sheets)
        {
            List<SheetAnalysis> ordered = sheets
                .Select(a => new
                {
                    Analysis = a,
                    Key = BuildSortKey(GetAnchor(a, SheetService.GetSheetName(a.Sheet))),
                    ExactFirst = SheetService.GetSheetName(a.Sheet).Trim() == a.SingleBaseName ? 0 : 1,
                    SheetName = SheetService.GetSheetName(a.Sheet).Trim()
                })
                .OrderBy(x => x.Key, KeyComparer)
                .ThenBy(x => x.ExactFirst)
                .ThenBy(x => x.SheetName, KeyComparer)
                .Select(x => x.Analysis)
                .ToList();

            Logger.Info("Sheet sort order:");
            foreach (SheetAnalysis analysis in ordered)
            {
                string anchor = GetAnchor(analysis, SheetService.GetSheetName(analysis.Sheet));
                string keyForLog = BuildSortKey(anchor).Replace(KeyPartSeparator, '|');
                Logger.Info($"  '{SheetService.GetSheetName(analysis.Sheet)}' (anchor: {anchor}, key: {keyForLog})");
            }

            return ordered;
        }

        private static bool IsAssemblyBaseName(string baseName, string fullName, HashSet<string> assemblyNames)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return false;
            }

            if (assemblyNames.Contains(baseName))
            {
                return true;
            }

            return !string.Equals(baseName, fullName, StringComparison.Ordinal)
                && fullName.StartsWith(baseName, StringComparison.Ordinal);
        }

        private static string GetAnchor(SheetAnalysis analysis, string sheetName)
        {
            if (analysis.SingleBaseName != null)
            {
                return analysis.SingleBaseName;
            }

            if (analysis.BaseNames != null && analysis.BaseNames.Count > 1)
            {
                string minName = null;
                foreach (string baseName in analysis.BaseNames)
                {
                    if (minName == null || KeyComparer.Compare(baseName, minName) < 0)
                    {
                        minName = baseName;
                    }
                }

                return minName;
            }

            string name = sheetName ?? string.Empty;
            int cutIndex = name.Length;

            foreach (char separator in SimpleSeparators)
            {
                int index = name.IndexOf(separator);
                if (index >= 0 && index < cutIndex)
                {
                    cutIndex = index;
                }
            }

            string anchor = cutIndex < name.Length ? name.Substring(0, cutIndex).Trim() : name.Trim();
            return string.IsNullOrEmpty(anchor) ? "Без имени" : anchor;
        }

        private static readonly char[] SimpleSeparators = { '-', ',', ';' };

        private const char KeyPartSeparator = '\u0001';

        private static string BuildSortKey(string anchor)
        {
            Match match = Regex.Match(anchor, @"(\d+)");
            if (match.Success)
            {
                string code = anchor.Substring(match.Index);
                return match.Groups[1].Value.PadLeft(12, '0') + KeyPartSeparator + code + KeyPartSeparator + anchor;
            }

            return "zzzzzzzzzzzz" + KeyPartSeparator + anchor;
        }

        private bool RenameSheet(ViewSheet sheet, string newName, HashSet<string> occupiedNames, List<string> renameLog)
        {
            if (string.IsNullOrEmpty(newName))
            {
                return false;
            }

            string sanitizedName = SheetService.SanitizeSheetName(newName);

            if (string.IsNullOrEmpty(sanitizedName))
            {
                Logger.Warn($"Sheet name '{newName}' is empty after sanitizing. Skipping rename of sheet '{SheetService.GetSheetName(sheet)}'.");
                return false;
            }

            string currentName = SheetService.GetSheetName(sheet).Trim();

            if (string.Equals(currentName, sanitizedName, StringComparison.Ordinal))
            {
                return false;
            }

            if (HasManualSuffix(currentName))
            {
                Logger.Info($"Sheet '{currentName}' has a manual suffix (начало/окончание/не размещенное). Skipping.");
                return false;
            }

            if (occupiedNames.Contains(sanitizedName))
            {
                int suffix = 2;
                string candidate = $"{sanitizedName} ({suffix})";
                while (occupiedNames.Contains(candidate))
                {
                    suffix++;
                    candidate = $"{sanitizedName} ({suffix})";
                }

                Logger.Warn($"Sheet name '{sanitizedName}' is already taken; using '{candidate}' instead.");
                sanitizedName = candidate;
            }

            try
            {
                Parameter nameParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NAME);
                if (nameParameter != null && !nameParameter.IsReadOnly)
                {
                    nameParameter.Set(sanitizedName);
                }
                else
                {
                    sheet.Name = sanitizedName;
                }

                occupiedNames.Add(sanitizedName);
                renameLog.Add($"«{currentName}» → «{sanitizedName}»");
                return true;
            }
            catch (ArgumentException)
            {
                Logger.Error($"Invalid characters in name '{sanitizedName}' (current sheet '{currentName}'): {LogCharCodes(newName)}");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not rename sheet '{currentName}': {ex.Message}");
                throw;
            }
        }

        private static readonly string[] ManualSheetSuffixes =
        {
            "(начало)",
            "(окончание)",
            SheetService.SignalSheetSuffix.Trim()
        };

        private static bool HasManualSuffix(string sheetName)
        {
            string name = sheetName.TrimEnd();
            foreach (string suffix in ManualSheetSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string LogCharCodes(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            foreach (char c in name)
            {
                builder.Append($"{(int)c:X4} ");
            }

            return $"chars ({builder.ToString().TrimEnd()})";
        }

        private void RenumberSheets(
            Document doc,
            List<SheetAnalysis> ordered,
            HashSet<ElementId> nonEditableSheetIds,
            IReadOnlyDictionary<ElementId, string> ownersById,
            int startNumber,
            List<string> renumberLog,
            List<string> warnings)
        {
            if (ordered.Count == 0)
            {
                return;
            }

            HashSet<string> remainingGroupIds = new HashSet<string>(ordered.Select(a => a.Sheet.Id.ToString()));
            HashSet<string> outsideNumbers = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, ElementId> occupiedByHolderIds = new Dictionary<string, ElementId>(StringComparer.Ordinal);

            foreach (ViewSheet sheet in _sheetService.GetSheets(doc))
            {
                if (remainingGroupIds.Contains(sheet.Id.ToString()))
                {
                    continue;
                }

                string number = (sheet.SheetNumber ?? string.Empty).Trim();
                outsideNumbers.Add(number);
                occupiedByHolderIds[number] = sheet.Id;
            }

            foreach (SheetAnalysis analysis in ordered)
            {
                if (!nonEditableSheetIds.Contains(analysis.Sheet.Id))
                {
                    continue;
                }

                string skippedNumber = analysis.Sheet.SheetNumber?.Trim();
                if (string.IsNullOrEmpty(skippedNumber))
                {
                    continue;
                }

                outsideNumbers.Add(skippedNumber);
                occupiedByHolderIds[skippedNumber] = analysis.Sheet.Id;
            }

            string tempPrefix = "TMP-";
            int tempIndex = 0;
            while (outsideNumbers.Contains($"{tempPrefix}{tempIndex:D4}"))
            {
                tempPrefix = $"TMP{tempIndex + 1}-";
                tempIndex++;
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                if (nonEditableSheetIds.Contains(ordered[i].Sheet.Id))
                {
                    continue;
                }

                try
                {
                    ordered[i].Sheet.SheetNumber = $"{tempPrefix}{i:D4}";
                }
                catch (Exception ex)
                {
                    Logger.Error($"Could not set temporary number for sheet '{SheetService.GetSheetName(ordered[i].Sheet)}': {ex}");
                    warnings.Add($"Не удалось установить временный номер листу «{SheetService.GetSheetName(ordered[i].Sheet)}»: {ex.Message}");
                }
            }

            int counter = startNumber;
            List<string> conflictedNumbers = new List<string>();
            List<string> numberingBreaks = new List<string>();
            int skippedSheetsCount = 0;

            foreach (SheetAnalysis analysis in ordered)
            {
                if (nonEditableSheetIds.Contains(analysis.Sheet.Id))
                {
                    skippedSheetsCount++;
                    string keptNumber = analysis.Sheet.SheetNumber?.Trim();
                    if (int.TryParse(keptNumber, out int keptValue) && keptValue != counter)
                    {
                        string owner = ownersById != null && ownersById.TryGetValue(analysis.Sheet.Id, out string sheetOwner)
                            ? sheetOwner
                            : _worksharingService.GetOwnerName(doc, analysis.Sheet.Id);
                        numberingBreaks.Add($"«{SheetService.GetSheetName(analysis.Sheet)}» — номер {keptNumber} вместо {counter} (владелец: {owner})");
                    }

                    continue;
                }

                string candidate = counter.ToString();
                int attempts = 0;

                while (outsideNumbers.Contains(candidate) && attempts < 10000)
                {
                    if (!conflictedNumbers.Contains(candidate)
                        && occupiedByHolderIds.TryGetValue(candidate, out ElementId holderId))
                    {
                        Element holder = doc.GetElement(holderId);
                        ViewSheet holderSheet = holder as ViewSheet;
                        string holderName = holderSheet != null ? SheetService.GetSheetName(holderSheet) : candidate;
                        string owner = ownersById != null && ownersById.TryGetValue(holderId, out string holderOwner)
                            ? holderOwner
                            : _worksharingService.GetOwnerName(doc, holderId);
                        conflictedNumbers.Add($"{candidate} — {holderName} ({owner})");
                    }

                    counter++;
                    attempts++;
                    candidate = counter.ToString();
                }

                string oldNumber = analysis.OldNumber ?? string.Empty;

                try
                {
                    analysis.Sheet.SheetNumber = candidate;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Could not set number '{candidate}' for sheet '{SheetService.GetSheetName(analysis.Sheet)}': {ex}");
                    warnings.Add($"Не удалось присвоить номер «{candidate}» листу «{SheetService.GetSheetName(analysis.Sheet)}»: {ex.Message}");
                    counter++;
                    continue;
                }

                renumberLog.Add($"«{oldNumber}» → «{candidate}»");

                Logger.Info($"Renumbered sheet '{SheetService.GetSheetName(analysis.Sheet)}' from '{oldNumber}' to '{candidate}'.");
                counter++;
            }

            if (conflictedNumbers.Count > 0)
            {
                warnings.Add("Занятые номера листов: №" + string.Join(", №", conflictedNumbers) + ".");
            }

            if (skippedSheetsCount == 0)
            {
                return;
            }

            if (numberingBreaks.Count > 0)
            {
                warnings.Add("Нумерация сбилась: " + string.Join("; ", numberingBreaks) + ". Освободите занятые листы/виды и повторите модуль.");
                return;
            }

            warnings.Add("Нумерация не сбилась (занятые листы сохранили номера, соответствующие порядку).");
        }

        private class SheetAnalysis
        {
            public ViewSheet Sheet { get; set; }
            public bool IsEmpty { get; set; }
            public bool IsSignal { get; set; }
            public HashSet<string> BaseNames { get; set; }
            public string OldNumber { get; set; }

            public string SingleBaseName
            {
                get { return BaseNames != null && BaseNames.Count == 1 ? BaseNames.First() : null; }
            }
        }

        private class SheetsSortResult
        {
            public int RenamedCount { get; set; }
            public int DeletedCount { get; set; }
            public int SignalSheetsDeletedCount { get; set; }
            public int SignalViewsDeletedCount { get; set; }
            public int RenumberedCount { get; set; }
        }
    }
}
