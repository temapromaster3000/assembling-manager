using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class OrchestratorService
    {
        private readonly AssemblyService _assemblyService;
        private readonly ParameterService _parameterService;
        private readonly ViewService _viewService;
        private readonly ScheduleService _scheduleService;
        private readonly FilterService _filterService;

        public OrchestratorService()
        {
            _assemblyService = new AssemblyService();
            _parameterService = new ParameterService();
            _viewService = new ViewService();
            _scheduleService = new ScheduleService();
            _filterService = new FilterService();
        }

        public ViewCreationResult GenerateViews(Document doc, Application app, ViewCreationOptions options, ViewConflictResolution resolution)
        {
            Logger.Info("OrchestratorService.GenerateViews started.");

            List<AssemblyInstance> assemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            Logger.Info($"Found {assemblies.Count} assemblies in model.");

            if (assemblies.Count == 0)
            {
                Logger.Error("No assemblies found in model.");
                throw new InvalidOperationException("В модели не найдены сборки.");
            }

            HashSet<Category> allCategories = new HashSet<Category>();
            Dictionary<AssemblyInstance, ICollection<ElementId>> assemblyElements = new Dictionary<AssemblyInstance, ICollection<ElementId>>();

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = _assemblyService.CollectAssemblyElements(doc, assembly);
                assemblyElements[assembly] = elementIds;

                foreach (ElementId elementId in elementIds)
                {
                    Element element = doc.GetElement(elementId);
                    if (element != null && element.Category != null)
                    {
                        allCategories.Add(element.Category);
                    }
                }
            }

            Logger.Info($"Collected {assemblyElements.Count} assemblies, {allCategories.Count} categories.");

            ElementId parameterId;

            if (options.UseExistingGroupingParameter)
            {
                Logger.Info("Using existing grouping parameter 'ADSK_Группирование'.");
                parameterId = _parameterService.GetParameterByName(doc, "ADSK_Группирование");
                if (parameterId == null)
                {
                    Logger.Error("Parameter 'ADSK_Группирование' not found in project.");
                    throw new InvalidOperationException("Параметр 'ADSK_Группирование' не найден в проекте.");
                }

                if (options.MissingCategoriesCount > 0)
                {
                    _parameterService.AddMissingCategories(doc, parameterId, allCategories);
                }
            }
            else if (options.CreateNewParameter)
            {
                Logger.Info("Creating new grouping parameter.");
                parameterId = _parameterService.GetOrCreateParameter(doc, app, allCategories);
            }
            else
            {
                Logger.Error("No grouping parameter option selected.");
                throw new InvalidOperationException("Не выбран способ работы с параметром для фильтра.");
            }

            Logger.Info("Parameter resolved.");
            Logger.Info($"Selected templates: Plan={options.PlanTemplateId}, Section={options.SectionTemplateId}, 3D={options.View3DTemplateId}, Schedule={options.ScheduleViewTemplateId}");

            ViewSchedule masterSchedule = null;
            if (options.CreateSchedule && options.MasterScheduleId.HasValue)
            {
#pragma warning disable CS0618
                ElementId masterScheduleId = new ElementId(options.MasterScheduleId.Value);
#pragma warning restore CS0618
                masterSchedule = doc.GetElement(masterScheduleId) as ViewSchedule;
                if (masterSchedule == null)
                {
                    Logger.Error($"Master schedule Id {options.MasterScheduleId.Value} not found.");
                    throw new InvalidOperationException("Выбранная мастер-спецификация не найдена в модели.");
                }

                Logger.Info($"Using master schedule '{masterSchedule.Name}' (Id {options.MasterScheduleId.Value}).");
            }

            ViewCreationResult result = new ViewCreationResult();
            Dictionary<string, View> viewMasters = new Dictionary<string, View>();
            Dictionary<string, List<View>> newViewsByAssembly = new Dictionary<string, List<View>>();
            bool createAnyView = options.CreatePlan ||
                                 options.CreateFrontView ||
                                 options.CreateBackView ||
                                 options.CreateRightView ||
                                 options.CreateLeftView ||
                                 options.Create3D;

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyElements[assembly];
                _parameterService.SetParameterValue(doc, parameterId, elementIds, assembly.Name);

                ElementId levelId = null;
                if (createAnyView)
                {
                    BoundingBoxXYZ bbox = _assemblyService.GetElementsBoundingBox(doc, elementIds, offset: 0.0);
                    if (bbox == null)
                    {
                        continue;
                    }

                    levelId = _assemblyService.GetOrCreateZeroLevelId(doc);

                    if (options.CreatePlan)
                    {
                        string suffix = ViewService.PlanSuffix;
                        View master = viewMasters.ContainsKey(ViewService.ViewKindPlan) ? viewMasters[ViewService.ViewKindPlan] : null;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.PlanTemplateId,
                            () => _viewService.CreatePlanView(doc, assembly.Name, bbox, levelId, options.PlanViewFamilyTypeId),
                            m => _viewService.DuplicatePlanView(doc, (ViewPlan)m, assembly.Name, bbox, levelId, options.PlanViewFamilyTypeId),
                            master,
                            resolution,
                            result,
                            typeof(ViewPlan),
                            ViewService.ViewKindPlan);
                        TrackNewView(newViewsByAssembly, assembly.Name, view, createdOrReplaced);
                        if (createdOrReplaced && !viewMasters.ContainsKey(ViewService.ViewKindPlan))
                            viewMasters[ViewService.ViewKindPlan] = view;
                    }

                    if (options.CreateFrontView)
                    {
                        string suffix = ViewService.FrontViewSuffix;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.SectionTemplateId,
                            () => _viewService.CreateFrontView(doc, assembly.Name, bbox, options.SectionViewFamilyTypeId),
                            m => _viewService.DuplicateSectionView(doc, (ViewSection)m, assembly.Name, ViewService.FrontViewSuffix, bbox, options.SectionViewFamilyTypeId),
                            null,
                            resolution,
                            result,
                            typeof(ViewSection),
                            ViewService.ViewKindFrontView);
                        TrackNewView(newViewsByAssembly, assembly.Name, view, createdOrReplaced);
                    }

                    if (options.CreateBackView)
                    {
                        string suffix = ViewService.BackViewSuffix;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.SectionTemplateId,
                            () => _viewService.CreateBackView(doc, assembly.Name, bbox, options.SectionViewFamilyTypeId),
                            m => _viewService.DuplicateSectionView(doc, (ViewSection)m, assembly.Name, ViewService.BackViewSuffix, bbox, options.SectionViewFamilyTypeId),
                            null,
                            resolution,
                            result,
                            typeof(ViewSection),
                            ViewService.ViewKindBackView);
                        TrackNewView(newViewsByAssembly, assembly.Name, view, createdOrReplaced);
                    }

                    if (options.CreateRightView)
                    {
                        string suffix = ViewService.RightViewSuffix;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.SectionTemplateId,
                            () => _viewService.CreateRightView(doc, assembly.Name, bbox, options.SectionViewFamilyTypeId),
                            m => _viewService.DuplicateSectionView(doc, (ViewSection)m, assembly.Name, ViewService.RightViewSuffix, bbox, options.SectionViewFamilyTypeId),
                            null,
                            resolution,
                            result,
                            typeof(ViewSection),
                            ViewService.ViewKindRightView);
                        TrackNewView(newViewsByAssembly, assembly.Name, view, createdOrReplaced);
                    }

                    if (options.CreateLeftView)
                    {
                        string suffix = ViewService.LeftViewSuffix;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.SectionTemplateId,
                            () => _viewService.CreateLeftView(doc, assembly.Name, bbox, options.SectionViewFamilyTypeId),
                            m => _viewService.DuplicateSectionView(doc, (ViewSection)m, assembly.Name, ViewService.LeftViewSuffix, bbox, options.SectionViewFamilyTypeId),
                            null,
                            resolution,
                            result,
                            typeof(ViewSection),
                            ViewService.ViewKindLeftView);
                        TrackNewView(newViewsByAssembly, assembly.Name, view, createdOrReplaced);
                    }

                    if (options.Create3D)
                    {
                        string suffix = ViewService.View3DSuffix;
                        View master = viewMasters.ContainsKey(ViewService.ViewKind3D) ? viewMasters[ViewService.ViewKind3D] : null;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.View3DTemplateId,
                            () => _viewService.Create3DView(doc, assembly.Name, bbox, options.View3DViewFamilyTypeId),
                            m => _viewService.Duplicate3DView(doc, (View3D)m, assembly.Name, bbox, options.View3DViewFamilyTypeId),
                            master,
                            resolution,
                            result,
                            typeof(View3D),
                            ViewService.ViewKind3D);
                        TrackNewView(newViewsByAssembly, assembly.Name, view, createdOrReplaced);
                        if (createdOrReplaced && !viewMasters.ContainsKey(ViewService.ViewKind3D))
                            viewMasters[ViewService.ViewKind3D] = view;
                    }
                }

                if (options.CreateSchedule && masterSchedule != null)
                {
                    CreateOrReplaceSchedule(
                        doc,
                        masterSchedule,
                        parameterId,
                        assembly.Name,
                        options.ScheduleViewTemplateId,
                        resolution,
                        result);
                }
            }

            Logger.Info("Creating and applying assembly filters.");

            ViewTemplateService viewTemplateService = new ViewTemplateService();

            foreach (AssemblyInstance assembly in assemblies)
            {
                (ParameterFilterElement assemblyFilter, bool assemblyFilterRecreated) = _filterService.EnsureAssemblyFilter(doc, parameterId, assembly.Name, allCategories);
                (ParameterFilterElement sectionMarkFilter, bool sectionMarkFilterRecreated) = _filterService.EnsureSectionMarkFilter(doc, assembly.Name);

                List<View> allAssemblyViews = _viewService.GetExistingAssemblyViews(doc, assembly.Name);
                List<View> newViews = GetAssemblyViews(newViewsByAssembly, assembly.Name);

                List<View> assemblyFilterTargets = assemblyFilterRecreated ? allAssemblyViews : newViews;
                List<View> sectionMarkFilterTargets = sectionMarkFilterRecreated
                    ? allAssemblyViews.Where(v => v is ViewPlan).ToList()
                    : newViews.Where(v => v is ViewPlan).ToList();

                foreach (View view in assemblyFilterTargets)
                {
                    if (view != null && view.IsValidObject)
                    {
                        _filterService.ApplyFilterToView(view, assemblyFilter.Id, assemblyFilter.Name);
                    }
                }

                foreach (View view in sectionMarkFilterTargets)
                {
                    if (view != null && view.IsValidObject)
                    {
                        _filterService.ApplyFilterToView(view, sectionMarkFilter.Id, sectionMarkFilter.Name);
                    }
                }

                foreach (View view in newViews.Where(v => v is View3D))
                {
                    _viewService.LockView(view);
                }

                VerifyAssemblyFilters(assembly.Name, allAssemblyViews, assemblyFilter, sectionMarkFilter, viewTemplateService);
            }

            Logger.Info($"OrchestratorService.GenerateViews finished: Created {result.CreatedCount}, Replaced {result.ReplacedCount}, Skipped {result.SkippedCount}.");

            return result;
        }

        private static List<View> GetAssemblyViews(Dictionary<string, List<View>> newViewsByAssembly, string assemblyName)
        {
            if (newViewsByAssembly.TryGetValue(assemblyName, out List<View> views))
            {
                return views;
            }

            return new List<View>();
        }

        private void TrackNewView(Dictionary<string, List<View>> newViewsByAssembly, string assemblyName, View view, bool createdOrReplaced)
        {
            if (!createdOrReplaced || view == null)
            {
                return;
            }

            if (!newViewsByAssembly.TryGetValue(assemblyName, out List<View> views))
            {
                views = new List<View>();
                newViewsByAssembly[assemblyName] = views;
            }

            views.Add(view);
        }

        private void VerifyAssemblyFilters(
            string assemblyName,
            List<View> allAssemblyViews,
            ParameterFilterElement assemblyFilter,
            ParameterFilterElement sectionMarkFilter,
            ViewTemplateService viewTemplateService)
        {
            int total = 0;
            int appliedCount = 0;
            List<string> missing = new List<string>();

            foreach (View view in allAssemblyViews)
            {
                if (view == null || !view.IsValidObject)
                {
                    continue;
                }

                total++;

                bool assemblyApplied = _filterService.IsFilterAppliedToView(view, assemblyFilter.Id);
                bool sectionMarkApplied = !(view is ViewPlan) || (sectionMarkFilter != null && _filterService.IsFilterAppliedToView(view, sectionMarkFilter.Id));

                if (assemblyApplied && sectionMarkApplied)
                {
                    appliedCount++;
                }
                else
                {
                    missing.Add(view.Name);

                    if (!assemblyApplied)
                    {
                        Logger.Warn($"Verification: view '{view.Name}' does not have filter '{assemblyFilter.Name}'.");
                    }

                    if (!sectionMarkApplied)
                    {
                        Logger.Warn($"Verification: view '{view.Name}' does not have filter '{sectionMarkFilter.Name}'.");
                    }
                }

                if (assemblyApplied && !_filterService.IsFilterVisibilityAppliedToView(view, assemblyFilter.Id))
                {
                    Logger.Warn($"Verification: filter '{assemblyFilter.Name}' is applied to view '{view.Name}' but is still visible (hiding was not applied).");
                }

                string lockingTemplate = viewTemplateService.GetFilterLockingTemplateName(view);
                if (lockingTemplate != null)
                {
                    Logger.Warn($"Verification: view '{view.Name}' has view template '{lockingTemplate}' that controls filters; filter settings may be blocked by this template.");
                }
            }

            Logger.Info($"Assembly '{assemblyName}': filter '{assemblyFilter.Name}' applied to {appliedCount}/{total} views; missing on: [{string.Join(", ", missing)}].");
        }

        private (View View, bool CreatedOrReplaced) CreateOrReplaceView(Document doc, string assemblyName, string suffix, int? templateId, Func<View> createFromScratch, Func<View, View> duplicateFromMaster, View master, ViewConflictResolution resolution, ViewCreationResult result, Type expectedViewType, string viewKind)
        {
            string viewName = assemblyName + suffix;
            View existingView = _viewService.GetViewByName(doc, viewName, expectedViewType);

            if (existingView != null)
            {
                ViewConflictItem conflict = resolution?.Items.FirstOrDefault(i => i.ViewName == viewName && i.ViewKind == viewKind);
                bool replace = conflict?.Replace ?? false;

                if (!replace)
                {
                    Logger.Debug($"Skipping existing view '{viewName}'.");
                    result.SkippedCount++;
                    return (existingView, false);
                }

                Logger.Debug($"Replacing existing view '{viewName}'.");
                _viewService.DeleteViewsByNames(doc, new[] { viewName }, expectedViewType);
                result.ReplacedCount++;
                View replacedView = CreateView(master, createFromScratch, duplicateFromMaster);
                _viewService.ApplyViewTemplate(replacedView, templateId);
                Logger.Debug($"Replaced view '{viewName}'.");
                return (replacedView, true);
            }

            PlannedViewItem skip = resolution?.SkipItems?.FirstOrDefault(i => i.ViewName == viewName && i.ViewKind == viewKind);
            if (skip != null)
            {
                Logger.Debug($"Skipping creation of view '{viewName}' (user choice).");
                result.SkippedCount++;
                return (null, false);
            }

            Logger.Debug($"Creating new view '{viewName}'.");
            result.CreatedCount++;
            View newView = CreateView(master, createFromScratch, duplicateFromMaster);
            _viewService.ApplyViewTemplate(newView, templateId);
            Logger.Debug($"Created view '{viewName}'.");
            return (newView, true);
        }

        private View CreateView(View master, Func<View> createFromScratch, Func<View, View> duplicateFromMaster)
        {
            if (master != null && master.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            {
                try
                {
                    View duplicatedView = duplicateFromMaster(master);
                    if (duplicatedView != null)
                    {
                        Logger.Debug($"Duplicated view from master '{master.Name}'.");
                        return duplicatedView;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not duplicate view from master '{master.Name}': {ex.Message}. Falling back to creation.");
                }
            }

            return createFromScratch();
        }

        private (ViewSchedule Schedule, bool CreatedOrReplaced) CreateOrReplaceSchedule(
            Document doc,
            ViewSchedule master,
            ElementId groupingParameterId,
            string assemblyName,
            int? scheduleTemplateId,
            ViewConflictResolution resolution,
            ViewCreationResult result)
        {
            string scheduleName = assemblyName + ScheduleService.ScheduleSuffix;
            View existingSchedule = _viewService.GetViewByName(doc, scheduleName, typeof(ViewSchedule));

            if (existingSchedule != null)
            {
                ViewConflictItem conflict = resolution?.Items.FirstOrDefault(i => i.ViewName == scheduleName && i.ViewKind == ViewService.ViewKindSchedule);
                bool replace = conflict?.Replace ?? false;

                if (!replace)
                {
                    Logger.Debug($"Skipping existing schedule '{scheduleName}'.");
                    result.SkippedCount++;
                    return (existingSchedule as ViewSchedule, false);
                }

                Logger.Debug($"Replacing existing schedule '{scheduleName}'.");
                _viewService.DeleteViewsByNames(doc, new[] { scheduleName });
                result.ReplacedCount++;
            }
            else
            {
                PlannedViewItem skip = resolution?.SkipItems?.FirstOrDefault(i => i.ViewName == scheduleName && i.ViewKind == ViewService.ViewKindSchedule);
                if (skip != null)
                {
                    Logger.Debug($"Skipping creation of schedule '{scheduleName}' (user choice).");
                    result.SkippedCount++;
                    return (null, false);
                }

                result.CreatedCount++;
            }

            ViewSchedule schedule = _scheduleService.DuplicateScheduleForAssembly(
                doc,
                master,
                groupingParameterId,
                assemblyName,
                scheduleTemplateId);

            Logger.Debug($"Created schedule '{scheduleName}'.");
            return (schedule, true);
        }
    }
}
