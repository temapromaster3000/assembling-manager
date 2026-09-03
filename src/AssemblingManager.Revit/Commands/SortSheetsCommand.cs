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

            List<SheetAnalysis> analyses = BuildAnalysis(document, selectedGroup, _sheetService.GetAssemblyNames(document));
            Logger.Info($"Group '{selectedGroup.Name}': {analyses.Count} sheets, {analyses.Count(a => a.IsEmpty)} empty.");

            List<SheetAnalysis> remaining = analyses.Where(a => !a.IsEmpty).ToList();
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

                        List<ElementId> emptySheetIds = analyses.Where(a => a.IsEmpty).Select(a => a.Sheet.Id).ToList();
                        if (emptySheetIds.Count > 0)
                        {
                            document.Delete(emptySheetIds);
                            result.DeletedCount = emptySheetIds.Count;
                            Logger.Info($"Deleted {result.DeletedCount} empty sheets.");
                        }

                        HashSet<string> occupiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (SheetAnalysis analysis in ordered)
                        {
                            occupiedNames.Add(SheetService.GetSheetName(analysis.Sheet).Trim());
                        }

                        HashSet<string> validAssemblyNames = _sheetService.GetAssemblyNames(document);

                        foreach (SheetAnalysis analysis in ordered)
                        {
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

                        RenumberSheets(document, ordered, startNumber, renumberLog, warnings);
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

            Logger.Info($"=== SortSheetsCommand finished in {stopwatch.Elapsed.TotalSeconds:F2} s: renamed {result.RenamedCount}, deleted {result.DeletedCount}, renumbered {result.RenumberedCount} ===");

            SheetsSortReportDialog reportDialog = new SheetsSortReportDialog(
                result.RenamedCount,
                result.DeletedCount,
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
            "(не размещенное)"
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

        private void RenumberSheets(Document doc, List<SheetAnalysis> ordered, int startNumber, List<string> renumberLog, List<string> warnings)
        {
            if (ordered.Count == 0)
            {
                return;
            }

            HashSet<string> remainingGroupIds = new HashSet<string>(ordered.Select(a => a.Sheet.Id.ToString()));
            HashSet<string> outsideNumbers = new HashSet<string>(StringComparer.Ordinal);

            foreach (ViewSheet sheet in _sheetService.GetSheets(doc))
            {
                if (!remainingGroupIds.Contains(sheet.Id.ToString()))
                {
                    outsideNumbers.Add((sheet.SheetNumber ?? string.Empty).Trim());
                }
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
            foreach (SheetAnalysis analysis in ordered)
            {
                string candidate = counter.ToString();
                int attempts = 0;

                while (outsideNumbers.Contains(candidate) && attempts < 10000)
                {
                    warnings.Add($"Номер «{candidate}» занят за пределами группы — использован следующий свободный.");
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
        }

        private class SheetAnalysis
        {
            public ViewSheet Sheet { get; set; }
            public bool IsEmpty { get; set; }
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
            public int RenumberedCount { get; set; }
        }
    }
}
