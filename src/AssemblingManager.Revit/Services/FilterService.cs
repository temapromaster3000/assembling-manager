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
            view.AddFilter(filterId);
            view.SetFilterVisibility(filterId, false);
        }
    }
}
