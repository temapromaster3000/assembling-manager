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
                        View master = viewMasters.ContainsKey(suffix) ? viewMasters[suffix] : null;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.PlanTemplateId,
                            () => _viewService.CreatePlanView(doc, assembly.Name, bbox, levelId, options.PlanViewFamilyTypeId),
                            m => _viewService.DuplicatePlanView(doc, (ViewPlan)m, assembly.Name, bbox, levelId, options.PlanViewFamilyTypeId),
                            master,
                            resolution,
                            result);
                        if (createdOrReplaced && !viewMasters.ContainsKey(suffix))
                            viewMasters[suffix] = view;
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
                            result);
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
                            result);
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
                            result);
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
                            result);
                    }

                    if (options.Create3D)
                    {
                        string suffix = ViewService.View3DSuffix;
                        View master = viewMasters.ContainsKey(suffix) ? viewMasters[suffix] : null;
                        (View view, bool createdOrReplaced) = CreateOrReplaceView(
                            doc,
                            assembly.Name,
                            suffix,
                            options.View3DTemplateId,
                            () => _viewService.Create3DView(doc, assembly.Name, bbox, options.View3DViewFamilyTypeId),
                            m => _viewService.Duplicate3DView(doc, (View3D)m, assembly.Name, bbox, options.View3DViewFamilyTypeId),
                            master,
                            resolution,
                            result);
                        if (createdOrReplaced && !viewMasters.ContainsKey(suffix))
                            viewMasters[suffix] = view;
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

            foreach (AssemblyInstance assembly in assemblies)
            {
                ParameterFilterElement assemblyFilter = _filterService.CreateAssemblyFilter(doc, parameterId, assembly.Name, allCategories);
                ParameterFilterElement sectionMarkFilter = _filterService.CreateSectionMarkFilter(doc, assembly.Name);

                List<View> allAssemblyViews = _viewService.GetExistingAssemblyViews(doc, assembly.Name);
                foreach (View view in allAssemblyViews)
                {
                    _filterService.ApplyFilterToView(view, assemblyFilter.Id);

                    if (view is ViewPlan)
                    {
                        _filterService.ApplyFilterToView(view, sectionMarkFilter.Id);
                    }
                }
            }

            Logger.Info($"OrchestratorService.GenerateViews finished: Created {result.CreatedCount}, Replaced {result.ReplacedCount}, Skipped {result.SkippedCount}.");

            return result;
        }

        private (View View, bool CreatedOrReplaced) CreateOrReplaceView(Document doc, string assemblyName, string suffix, int? templateId, Func<View> createFromScratch, Func<View, View> duplicateFromMaster, View master, ViewConflictResolution resolution, ViewCreationResult result)
        {
            string viewName = assemblyName + suffix;
            View existingView = _viewService.GetViewByName(doc, viewName);

            if (existingView != null)
            {
                ViewConflictItem conflict = resolution?.Items.FirstOrDefault(i => i.ViewName == viewName);
                bool replace = conflict?.Replace ?? false;

                if (!replace)
                {
                    Logger.Debug($"Skipping existing view '{viewName}'.");
                    result.SkippedCount++;
                    return (existingView, false);
                }

                Logger.Debug($"Replacing existing view '{viewName}'.");
                _viewService.DeleteViewsByNames(doc, new[] { viewName });
                result.ReplacedCount++;
                View replacedView = CreateView(master, createFromScratch, duplicateFromMaster);
                _viewService.ApplyViewTemplate(replacedView, templateId);
                Logger.Debug($"Replaced view '{viewName}'.");
                return (replacedView, true);
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
            View existingSchedule = _viewService.GetViewByName(doc, scheduleName);

            if (existingSchedule != null)
            {
                ViewConflictItem conflict = resolution?.Items.FirstOrDefault(i => i.ViewName == scheduleName);
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
