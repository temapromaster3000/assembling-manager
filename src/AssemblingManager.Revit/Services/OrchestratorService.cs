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
        private readonly FilterService _filterService;

        public OrchestratorService()
        {
            _assemblyService = new AssemblyService();
            _parameterService = new ParameterService();
            _viewService = new ViewService();
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
            Logger.Info($"Selected templates: Plan={options.PlanTemplateId}, Section={options.SectionTemplateId}, 3D={options.View3DTemplateId}");

            ViewCreationResult result = new ViewCreationResult();

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyElements[assembly];
                _parameterService.SetParameterValue(doc, parameterId, elementIds, assembly.Name);

                BoundingBoxXYZ bbox = _assemblyService.GetElementsBoundingBox(doc, elementIds, offset: 0.0);
                if (bbox == null)
                {
                    continue;
                }

                bool hasSelectedViewType = options.CreatePlan ||
                                           options.CreateFrontView ||
                                           options.CreateBackView ||
                                           options.CreateRightView ||
                                           options.CreateLeftView ||
                                           options.Create3D;

                if (!hasSelectedViewType)
                {
                    continue;
                }

                if (options.CreatePlan)
                {
                    CreateOrReplaceView(doc, assembly.Name, ViewService.PlanSuffix, options.PlanTemplateId, () =>
                    {
                        ElementId levelId = _assemblyService.GetOrCreateZeroLevelId(doc);
                        return _viewService.CreatePlanView(doc, assembly.Name, bbox, levelId);
                    }, resolution, result);
                }

                if (options.CreateFrontView)
                {
                    CreateOrReplaceView(doc, assembly.Name, ViewService.FrontViewSuffix, options.SectionTemplateId, () =>
                        _viewService.CreateFrontView(doc, assembly.Name, bbox), resolution, result);
                }

                if (options.CreateBackView)
                {
                    CreateOrReplaceView(doc, assembly.Name, ViewService.BackViewSuffix, options.SectionTemplateId, () =>
                        _viewService.CreateBackView(doc, assembly.Name, bbox), resolution, result);
                }

                if (options.CreateRightView)
                {
                    CreateOrReplaceView(doc, assembly.Name, ViewService.RightViewSuffix, options.SectionTemplateId, () =>
                        _viewService.CreateRightView(doc, assembly.Name, bbox), resolution, result);
                }

                if (options.CreateLeftView)
                {
                    CreateOrReplaceView(doc, assembly.Name, ViewService.LeftViewSuffix, options.SectionTemplateId, () =>
                        _viewService.CreateLeftView(doc, assembly.Name, bbox), resolution, result);
                }

                if (options.Create3D)
                {
                    CreateOrReplaceView(doc, assembly.Name, ViewService.View3DSuffix, options.View3DTemplateId, () =>
                        _viewService.Create3DView(doc, assembly.Name, bbox), resolution, result);
                }
            }

            Logger.Info("Creating and applying assembly filters.");

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyElements[assembly];
                List<Category> assemblyCategories = elementIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null)
                    .Select(e => e.Category)
                    .Distinct()
                    .ToList();

                ParameterFilterElement filter = _filterService.CreateAssemblyFilter(doc, parameterId, assembly.Name, assemblyCategories);

                List<View> allAssemblyViews = _viewService.GetExistingAssemblyViews(doc, assembly.Name);
                foreach (View view in allAssemblyViews)
                {
                    _filterService.ApplyFilterToView(view, filter.Id);
                }
            }

            Logger.Info($"OrchestratorService.GenerateViews finished: Created {result.CreatedCount}, Replaced {result.ReplacedCount}, Skipped {result.SkippedCount}.");

            return result;
        }

        private View CreateOrReplaceView(Document doc, string assemblyName, string suffix, int? templateId, Func<View> createView, ViewConflictResolution resolution, ViewCreationResult result)
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
                    return existingView;
                }

                Logger.Debug($"Replacing existing view '{viewName}'.");
                _viewService.DeleteViewsByNames(doc, new[] { viewName });
                result.ReplacedCount++;
                View replacedView = createView();
                _viewService.ApplyViewTemplate(replacedView, templateId);
                Logger.Debug($"Replaced view '{viewName}'.");
                return replacedView;
            }

            Logger.Debug($"Creating new view '{viewName}'.");
            result.CreatedCount++;
            View newView = createView();
            _viewService.ApplyViewTemplate(newView, templateId);
            Logger.Debug($"Created new view '{viewName}'.");
            return newView;
        }
    }
}
