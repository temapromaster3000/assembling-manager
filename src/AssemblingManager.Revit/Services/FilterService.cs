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

#if REVIT2023_OR_GREATER
            FilterRule rule = ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, assemblyName);
#else
            FilterRule rule = ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, assemblyName, true);
#endif
            ElementParameterFilter elementFilter = new ElementParameterFilter(rule);

            ParameterFilterElement filter = ParameterFilterElement.Create(doc, filterName, categoryIds, elementFilter);

#pragma warning disable CS0618
            int filterIdValue = filter.Id.IntegerValue;
#pragma warning restore CS0618
            Logger.Info($"Created assembly filter '{filterName}' (Id {filterIdValue}): rule AssemblyParameter != '{assemblyName}', {categoryIds.Count} categories.");

            return filter;
        }

        public ParameterFilterElement CreateSectionMarkFilter(Document doc, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new ArgumentException("Имя сборки не может быть пустым.", nameof(assemblyName));
            }

            string filterName = $"{assemblyName}_СкрытьЧужиеРазрезы";

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

#pragma warning disable CS0618
            int filterIdValue = filter.Id.IntegerValue;
#pragma warning restore CS0618
            Logger.Info($"Created section mark filter '{filterName}' (Id {filterIdValue}): rule VIEW_NAME not contains '{filterValue}', {categoryIds.Count} categories.");

            return filter;
        }

        public ParameterFilterElement FindFilterByName(Document doc, string filterName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => f.Name == filterName);
        }

        public (ParameterFilterElement Filter, bool Recreated) EnsureAssemblyFilter(Document doc, ElementId parameterId, string assemblyName, ICollection<Category> categories)
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
            ParameterFilterElement existing = FindFilterByName(doc, filterName);

            if (existing == null)
            {
                ParameterFilterElement created = CreateAssemblyFilter(doc, parameterId, assemblyName, categories);
                return (created, true);
            }

            HashSet<ElementId> currentCategoryIds = new HashSet<ElementId>(existing.GetCategories());
            bool categoriesMatch = currentCategoryIds.SetEquals(categoryIds);

            if (!categoriesMatch)
            {
                Logger.Warn($"Assembly filter '{filterName}' category set changed ({currentCategoryIds.Count} -> {categoryIds.Count} categories). Recreating filter.");
                Logger.Warn("Worksharing: deleting the filter removes it from all views it was assigned to, including views owned by other users. Re-assignment to those views may fail because they are editable only by their owners.");

                DeleteExistingFilter(doc, filterName);
                ParameterFilterElement created = CreateAssemblyFilter(doc, parameterId, assemblyName, categories);
                return (created, true);
            }

#if REVIT2023_OR_GREATER
            FilterRule rule = ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, assemblyName);
#else
            FilterRule rule = ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, assemblyName, true);
#endif
            ElementParameterFilter elementFilter = new ElementParameterFilter(rule);

            try
            {
                existing.SetElementFilter(elementFilter);

#pragma warning disable CS0618
                int existingIdValue = existing.Id.IntegerValue;
#pragma warning restore CS0618
                Logger.Info($"Reused assembly filter '{filterName}' (Id {existingIdValue}): rules updated in place, existing view assignments preserved.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not update rules of assembly filter '{filterName}': {ex}. Existing view assignments preserved.");
            }

            return (existing, false);
        }

        public (ParameterFilterElement Filter, bool Recreated) EnsureSectionMarkFilter(Document doc, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new ArgumentException("Имя сборки не может быть пустым.", nameof(assemblyName));
            }

            string filterName = $"{assemblyName}_СкрытьЧужиеРазрезы";
            ParameterFilterElement existing = FindFilterByName(doc, filterName);

            if (existing != null)
            {
#pragma warning disable CS0618
                int existingIdValue = existing.Id.IntegerValue;
#pragma warning restore CS0618
                Logger.Info($"Reused section mark filter '{filterName}' (Id {existingIdValue}), no changes needed.");
                return (existing, false);
            }

            ParameterFilterElement created = CreateSectionMarkFilter(doc, assemblyName);
            return (created, true);
        }

        public void DeleteExistingFilter(Document doc, string filterName)
        {
            ParameterFilterElement existingFilter = FindFilterByName(doc, filterName);

            if (existingFilter != null)
            {
#pragma warning disable CS0618
                int filterIdValue = existingFilter.Id.IntegerValue;
#pragma warning restore CS0618
                Logger.Info($"Deleting filter '{filterName}' (old Id {filterIdValue}).");
                doc.Delete(existingFilter.Id);
            }
        }

        public void ApplyFilterToView(View view, ElementId filterId, string filterName)
        {
            try
            {
                view.AddFilter(filterId);
                view.SetFilterVisibility(filterId, false);
                Logger.Debug($"Applied filter '{filterName}' to view '{view.Name}'.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not apply filter '{filterName}' to view '{view.Name}': {ex}");
            }
        }

        public bool IsFilterAppliedToView(View view, ElementId filterId)
        {
            return view.GetFilters().Contains(filterId);
        }

        public bool IsFilterVisibilityAppliedToView(View view, ElementId filterId)
        {
            return !view.GetFilterVisibility(filterId);
        }
    }
}
