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
            List<AssemblyInstance> assemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0)
            {
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

            ElementId parameterId = _parameterService.GetOrCreateParameter(doc, app, allCategories);

            ViewCreationResult result = new ViewCreationResult();

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyElements[assembly];

                BoundingBoxXYZ bbox = _assemblyService.GetElementsBoundingBox(doc, elementIds, offset: 0.0);
                if (bbox == null)
                {
                    continue;
                }

                List<View> viewsToFilter = new List<View>();
                bool anyViewModified = false;

                if (options.CreatePlan)
                {
                    View view = CreateOrReplaceView(doc, assembly.Name, ViewService.PlanSuffix, () =>
                    {
                        ElementId levelId = _assemblyService.GetOrCreateZeroLevelId(doc);
                        return _viewService.CreatePlanView(doc, assembly.Name, bbox, levelId);
                    }, resolution, result, out bool wasModified);

                    if (view != null)
                    {
                        viewsToFilter.Add(view);
                        anyViewModified |= wasModified;
                    }
                }

                if (options.CreateFrontView)
                {
                    View view = CreateOrReplaceView(doc, assembly.Name, ViewService.FrontViewSuffix, () =>
                        _viewService.CreateFrontView(doc, assembly.Name, bbox), resolution, result, out bool wasModified);

                    if (view != null)
                    {
                        viewsToFilter.Add(view);
                        anyViewModified |= wasModified;
                    }
                }

                if (options.CreateBackView)
                {
                    View view = CreateOrReplaceView(doc, assembly.Name, ViewService.BackViewSuffix, () =>
                        _viewService.CreateBackView(doc, assembly.Name, bbox), resolution, result, out bool wasModified);

                    if (view != null)
                    {
                        viewsToFilter.Add(view);
                        anyViewModified |= wasModified;
                    }
                }

                if (options.CreateRightView)
                {
                    View view = CreateOrReplaceView(doc, assembly.Name, ViewService.RightViewSuffix, () =>
                        _viewService.CreateRightView(doc, assembly.Name, bbox), resolution, result, out bool wasModified);

                    if (view != null)
                    {
                        viewsToFilter.Add(view);
                        anyViewModified |= wasModified;
                    }
                }

                if (options.CreateLeftView)
                {
                    View view = CreateOrReplaceView(doc, assembly.Name, ViewService.LeftViewSuffix, () =>
                        _viewService.CreateLeftView(doc, assembly.Name, bbox), resolution, result, out bool wasModified);

                    if (view != null)
                    {
                        viewsToFilter.Add(view);
                        anyViewModified |= wasModified;
                    }
                }

                if (options.Create3D)
                {
                    View view = CreateOrReplaceView(doc, assembly.Name, ViewService.View3DSuffix, () =>
                        _viewService.Create3DView(doc, assembly.Name, bbox), resolution, result, out bool wasModified);

                    if (view != null)
                    {
                        viewsToFilter.Add(view);
                        anyViewModified |= wasModified;
                    }
                }

                if (!anyViewModified)
                {
                    continue;
                }

                _parameterService.SetParameterValue(doc, parameterId, elementIds, assembly.Name);

                List<Category> assemblyCategories = elementIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null)
                    .Select(e => e.Category)
                    .Distinct()
                    .ToList();

                ParameterFilterElement filter = _filterService.CreateAssemblyFilter(doc, parameterId, assembly.Name, assemblyCategories);

                foreach (View view in viewsToFilter)
                {
                    _filterService.ApplyFilterToView(view, filter.Id);
                }
            }

            return result;
        }

        private View CreateOrReplaceView(Document doc, string assemblyName, string suffix, Func<View> createView, ViewConflictResolution resolution, ViewCreationResult result, out bool wasModified)
        {
            string viewName = assemblyName + suffix;
            View existingView = _viewService.GetViewByName(doc, viewName);

            if (existingView != null)
            {
                ViewConflictItem conflict = resolution?.Items.FirstOrDefault(i => i.ViewName == viewName);
                bool replace = conflict?.Replace ?? false;

                if (!replace)
                {
                    result.SkippedCount++;
                    wasModified = false;
                    return existingView;
                }

                _viewService.DeleteViewsByNames(doc, new[] { viewName });
                result.ReplacedCount++;
                wasModified = true;
                return createView();
            }

            result.CreatedCount++;
            wasModified = true;
            return createView();
        }
    }
}
