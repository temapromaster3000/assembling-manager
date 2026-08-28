using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
    public class RenameAssembliesCommand : IExternalCommand
    {
        private const string AssemblyParameterName = "AssemblyParameter";
        private const string AssemblyFilterSuffix = "_Фильтр";
        private const string SectionMarkFilterSuffix = "_СкрытьЧужиеРазрезы";

        private static readonly List<SuffixInfo> SuffixInfos = new List<SuffixInfo>
        {
            new SuffixInfo(typeof(ViewPlan), ViewService.PlanSuffix),
            new SuffixInfo(typeof(ViewSection), ViewService.FrontViewSuffix),
            new SuffixInfo(typeof(ViewSection), ViewService.BackViewSuffix),
            new SuffixInfo(typeof(ViewSection), ViewService.RightViewSuffix),
            new SuffixInfo(typeof(ViewSection), ViewService.LeftViewSuffix),
            new SuffixInfo(typeof(View3D), ViewService.View3DSuffix),
            new SuffixInfo(typeof(ViewSchedule), ScheduleService.ScheduleSuffix)
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            Document document = uiApplication.ActiveUIDocument?.Document;

            if (document == null)
            {
                MessageBox.Show("Активный документ не найден.", Constants.PluginName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return Result.Cancelled;
            }

            Logger.Info("=== RenameAssembliesCommand started ===");
            Logger.Info($"Document: {document.Title}");

            List<AssemblyInstance> assemblies = new FilteredElementCollector(document)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0)
            {
                Logger.Warn("No assemblies found. Command cancelled.");
                MessageBox.Show("В модели не найдены сборки.", Constants.PluginName, MessageBoxButton.OK, MessageBoxImage.Information);
                return Result.Cancelled;
            }

            Logger.Info($"Found {assemblies.Count} assemblies.");

            ParameterService parameterService = new ParameterService();
            ElementId parameterId = parameterService.GetParameterByName(document, AssemblyParameterName);

            if (parameterId == null)
            {
                Logger.Warn($"Parameter '{AssemblyParameterName}' not found. Command cancelled.");
                MessageBox.Show(
                    $"Параметр '{AssemblyParameterName}' не найден в проекте. Модуль «Переименовать сборки» работает со сборками, сформированными плагином.",
                    Constants.PluginName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return Result.Cancelled;
            }

            AssemblyService assemblyService = new AssemblyService();
            HashSet<string> currentAssemblyNames = new HashSet<string>(
                assemblies.Select(a => a.Name),
                StringComparer.Ordinal);

            List<AssemblyRenameInfo> renames = new List<AssemblyRenameInfo>();

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyService.CollectAssemblyElements(document, assembly);
                HashSet<string> values = parameterService.GetDistinctParameterValues(document, parameterId, elementIds);

                List<string> oldNames = new List<string>();

                foreach (string value in values)
                {
                    if (string.IsNullOrWhiteSpace(value) || string.Compare(value, assembly.Name, StringComparison.Ordinal) == 0)
                    {
                        continue;
                    }

                    if (currentAssemblyNames.Contains(value))
                    {
                        Logger.Warn($"Skipping value '{value}' of assembly '{assembly.Name}': it matches an existing assembly name.");
                        continue;
                    }

                    oldNames.Add(value);
                }

                if (oldNames.Count == 0)
                {
                    continue;
                }

                renames.Add(new AssemblyRenameInfo(assembly, elementIds, oldNames));
            }

            Logger.Info($"Detected {renames.Count} renamed assemblies.");

            if (renames.Count == 0)
            {
                MessageBox.Show("Несоответствий не обнаружено.", Constants.PluginName, MessageBoxButton.OK, MessageBoxImage.Information);
                return Result.Cancelled;
            }

            List<RenameReportItem> pendingItems = new List<RenameReportItem>();
            foreach (AssemblyRenameInfo rename in renames)
            {
                foreach (string oldName in rename.OldNames)
                {
                    pendingItems.Add(new RenameReportItem
                    {
                        OldName = oldName,
                        NewName = rename.Assembly.Name
                    });
                }
            }

            ConfirmRenameDialog confirmDialog = new ConfirmRenameDialog(renames.Count, pendingItems);
            if (confirmDialog.ShowDialog() != true)
            {
                Logger.Info("User cancelled the rename confirmation.");
                return Result.Cancelled;
            }

            List<RenameReportItem> reportItems = new List<RenameReportItem>();

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();
                Logger.Info("TransactionGroup started.");

                try
                {
                    using (Transaction transaction = new Transaction(document, "Переименовать сборки"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();
                        Logger.Info("Transaction started.");

                        ViewService viewService = new ViewService();
                        FilterService filterService = new FilterService();
                        ScheduleService scheduleService = new ScheduleService();

                        foreach (AssemblyRenameInfo rename in renames)
                        {
                            ProcessRename(document, rename, parameterId, parameterService, viewService, filterService, scheduleService, reportItems);
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

            try
            {
                using (Transaction refreshTransaction = new Transaction(document, "Обновить спецификации"))
                {
                    FailureHandlingOptions refreshOptions = refreshTransaction.GetFailureHandlingOptions();
                    refreshOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                    refreshTransaction.SetFailureHandlingOptions(refreshOptions);

                    refreshTransaction.Start();
                    Logger.Info("Refresh transaction started.");

                    RefreshRenamedSchedules(document, renames);

                    refreshTransaction.Commit();
                    Logger.Info("Refresh transaction committed.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception during schedule refresh: {ex}");
                message = $"Сборки переименованы, но не удалось обновить спецификации: {ex.Message}";
                return Result.Failed;
            }

            Logger.Info($"=== RenameAssembliesCommand finished: {renames.Count} assemblies renamed ===");

            RenameReportDialog reportDialog = new RenameReportDialog(renames.Count, reportItems);
            reportDialog.ShowDialog();

            return Result.Succeeded;
        }

        private static void RefreshRenamedSchedules(Document doc, List<AssemblyRenameInfo> renames)
        {
            ViewService viewService = new ViewService();
            int refreshed = 0;

            foreach (AssemblyRenameInfo rename in renames)
            {
                string scheduleName = rename.Assembly.Name + ScheduleService.ScheduleSuffix;
                ViewSchedule schedule = viewService.GetViewByName(doc, scheduleName, typeof(ViewSchedule)) as ViewSchedule;

                if (schedule == null || !schedule.IsValidObject)
                {
                    continue;
                }

                if (schedule.IsDataOutOfDate())
                {
                    schedule.RefreshData();
                    refreshed++;
                }
            }

            if (refreshed > 0)
            {
                doc.Regenerate();
            }

            Logger.Info($"Refreshed {refreshed} outdated schedules after rename.");
        }

        private void ProcessRename(
            Document doc,
            AssemblyRenameInfo rename,
            ElementId parameterId,
            ParameterService parameterService,
            ViewService viewService,
            FilterService filterService,
            ScheduleService scheduleService,
            List<RenameReportItem> reportItems)
        {
            foreach (string oldName in rename.OldNames)
            {
                Logger.Info($"Processing rename of assembly '{rename.Assembly.Name}': old name '{oldName}'.");

                foreach (SuffixInfo suffixInfo in SuffixInfos)
                {
                    string oldViewName = oldName + suffixInfo.Suffix;
                    string newViewName = rename.Assembly.Name + suffixInfo.Suffix;

                    View view = viewService.GetViewByName(doc, oldViewName, suffixInfo.ViewType);
                    if (view == null)
                    {
                        continue;
                    }

                    try
                    {
                        viewService.UnlockView(view);
                        view.Name = newViewName;
                        Logger.Info($"Renamed view '{oldViewName}' to '{newViewName}'.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not rename view '{oldViewName}': {ex.Message}");
                    }
                }

                filterService.DeleteExistingFilter(doc, oldName + AssemblyFilterSuffix);
                filterService.DeleteExistingFilter(doc, oldName + SectionMarkFilterSuffix);

                reportItems.Add(new RenameReportItem
                {
                    OldName = oldName,
                    NewName = rename.Assembly.Name
                });
            }

            parameterService.SetParameterValue(doc, parameterId, rename.ElementIds, rename.Assembly.Name);
            Logger.Info($"Updated parameter '{AssemblyParameterName}' for {rename.ElementIds.Count} elements of assembly '{rename.Assembly.Name}'.");

            ViewSchedule schedule = viewService.GetViewByName(
                doc,
                rename.Assembly.Name + ScheduleService.ScheduleSuffix,
                typeof(ViewSchedule)) as ViewSchedule;

            if (schedule != null)
            {
                if (scheduleService.UpdateScheduleFilter(doc, schedule, parameterId, rename.Assembly.Name))
                {
                    Logger.Info($"Updated schedule filter of '{schedule.Name}' to '{rename.Assembly.Name}'.");
                }
                else
                {
                    Logger.Warn($"Could not update schedule filter of '{schedule.Name}'.");
                }
            }

            CreateAndApplyNewFilters(doc, rename, parameterId, filterService, viewService);
        }

        private void CreateAndApplyNewFilters(Document doc, AssemblyRenameInfo rename, ElementId parameterId, FilterService filterService, ViewService viewService)
        {
            HashSet<Category> categories = new HashSet<Category>();

            foreach (ElementId elementId in rename.ElementIds)
            {
                Element element = doc.GetElement(elementId);
                if (element != null && element.Category != null)
                {
                    categories.Add(element.Category);
                }
            }

            try
            {
                ParameterFilterElement assemblyFilter = filterService.CreateAssemblyFilter(doc, parameterId, rename.Assembly.Name, categories);
                ParameterFilterElement sectionMarkFilter = filterService.CreateSectionMarkFilter(doc, rename.Assembly.Name);

                List<View> allAssemblyViews = viewService.GetExistingAssemblyViews(doc, rename.Assembly.Name);
                foreach (View view in allAssemblyViews)
                {
                    filterService.ApplyFilterToView(view, assemblyFilter.Id);

                    if (view is ViewPlan)
                    {
                        filterService.ApplyFilterToView(view, sectionMarkFilter.Id);
                    }

                    if (view is View3D)
                    {
                        viewService.LockView(view);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not recreate filters for assembly '{rename.Assembly.Name}': {ex.Message}");
            }
        }

        private class AssemblyRenameInfo
        {
            public AssemblyInstance Assembly { get; }
            public ICollection<ElementId> ElementIds { get; }
            public List<string> OldNames { get; }

            public AssemblyRenameInfo(AssemblyInstance assembly, ICollection<ElementId> elementIds, List<string> oldNames)
            {
                Assembly = assembly;
                ElementIds = elementIds;
                OldNames = oldNames;
            }
        }

        private class SuffixInfo
        {
            public Type ViewType { get; }
            public string Suffix { get; }

            public SuffixInfo(Type viewType, string suffix)
            {
                ViewType = viewType;
                Suffix = suffix;
            }
        }
    }
}
