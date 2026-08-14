using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class FilterService
    {
        private const string ParameterName = "AssemblyParameter";

        public ParameterFilterElement CreateAssemblyFilter(Document doc, ElementId parameterId, string assemblyName, ICollection<Category> categories)
        {
            List<ElementId> categoryIds = categories
                .Where(c => c != null)
                .Select(c => c.Id)
                .Distinct()
                .ToList();

            if (categoryIds.Count == 0)
            {
                throw new InvalidOperationException($"Нет категорий для фильтра сборки '{assemblyName}'.");
            }

            string filterName = $"{assemblyName}_Фильтр";

            DeleteExistingFilter(doc, filterName);

#if REVIT2023_OR_GREATER
            FilterRule rule = ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, assemblyName);
#else
            FilterRule rule = ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, assemblyName, true);
#endif
            ElementParameterFilter elementFilter = new ElementParameterFilter(rule);

            ParameterFilterElement filter = ParameterFilterElement.Create(doc, filterName, categoryIds, elementFilter);

            return filter;
        }

        public ParameterFilterElement CreateSectionMarkFilter(Document doc, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new ArgumentException("Имя сборки не может быть пустым.", nameof(assemblyName));
            }

            string filterName = $"{assemblyName}_СкрытьЧужиеРазрезы";

            DeleteExistingFilter(doc, filterName);

            List<ElementId> categoryIds = new List<ElementId>
            {
                new ElementId(BuiltInCategory.OST_Sections)
            };

            ElementId viewNameParameterId = new ElementId(BuiltInParameter.VIEW_NAME);

            string filterValue = assemblyName + "_";

#if REVIT2023_OR_GREATER
            FilterRule rule = ParameterFilterRuleFactory.CreateNotContainsRule(viewNameParameterId, filterValue);
#else
            FilterRule rule = ParameterFilterRuleFactory.CreateNotContainsRule(viewNameParameterId, filterValue, true);
#endif
            ElementParameterFilter elementFilter = new ElementParameterFilter(rule);

            ParameterFilterElement filter = ParameterFilterElement.Create(doc, filterName, categoryIds, elementFilter);

            return filter;
        }

        public void DeleteExistingFilter(Document doc, string filterName)
        {
            ParameterFilterElement existingFilter = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => f.Name == filterName);

            if (existingFilter != null)
            {
                doc.Delete(existingFilter.Id);
            }
        }

        public void ApplyFilterToView(View view, ElementId filterId)
        {
            try
            {
                view.AddFilter(filterId);
                view.SetFilterVisibility(filterId, false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not apply filter '{filterId}' to view '{view.Name}': {ex.Message}");
            }
        }
    }
}
