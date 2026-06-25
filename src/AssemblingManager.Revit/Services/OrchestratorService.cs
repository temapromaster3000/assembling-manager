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

        public void GenerateViews(Document doc, Application app, ViewCreationOptions options)
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

            foreach (AssemblyInstance assembly in assemblies)
            {
                ICollection<ElementId> elementIds = assemblyElements[assembly];
                _parameterService.SetParameterValue(doc, parameterId, elementIds, assembly.Name);

                _viewService.DeleteExistingViews(doc, assembly.Name);

                BoundingBoxXYZ bbox = _assemblyService.GetElementsBoundingBox(doc, elementIds, offset: 0.0);
                if (bbox == null)
                {
                    continue;
                }

                List<View> views = new List<View>();

                if (options.CreatePlan)
                {
                    ElementId levelId = _assemblyService.GetOrCreateZeroLevelId(doc);
                    views.Add(_viewService.CreatePlanView(doc, assembly.Name, bbox, levelId));
                }

                if (options.CreateFrontView)
                {
                    views.Add(_viewService.CreateFrontView(doc, assembly.Name, bbox));
                }

                if (options.CreateBackView)
                {
                    views.Add(_viewService.CreateBackView(doc, assembly.Name, bbox));
                }

                if (options.CreateRightView)
                {
                    views.Add(_viewService.CreateRightView(doc, assembly.Name, bbox));
                }

                if (options.CreateLeftView)
                {
                    views.Add(_viewService.CreateLeftView(doc, assembly.Name, bbox));
                }

                if (options.Create3D)
                {
                    views.Add(_viewService.Create3DView(doc, assembly.Name, bbox));
                }

                List<Category> assemblyCategories = elementIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null)
                    .Select(e => e.Category)
                    .Distinct()
                    .ToList();

                ParameterFilterElement filter = _filterService.CreateAssemblyFilter(doc, parameterId, assembly.Name, assemblyCategories);

                foreach (View view in views)
                {
                    _filterService.ApplyFilterToView(view, filter.Id);
                }
            }
        }
    }
}
