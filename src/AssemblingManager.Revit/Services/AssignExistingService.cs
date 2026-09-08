using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class AssignExistingService
    {
        private const string FilterSuffix = "_Фильтр";

        private readonly ParameterService _parameterService = new ParameterService();
        private readonly FilterService _filterService = new FilterService();

        public HashSet<Category> CollectCategories(Document doc, ICollection<ElementId> elementIds)
        {
            HashSet<Category> categories = new HashSet<Category>();

            foreach (ElementId elementId in elementIds)
            {
                Element element = doc.GetElement(elementId);
                if (element != null && element.Category != null)
                {
                    categories.Add(element.Category);
                }
            }

            return categories;
        }

        public List<string> GetMissingFilterCategoryNames(Document doc, string assemblyName, ICollection<Category> categories)
        {
            ParameterFilterElement existing = _filterService.FindFilterByName(doc, assemblyName + FilterSuffix);
            if (existing == null)
            {
                return new List<string>();
            }

            HashSet<ElementId> currentCategoryIds = new HashSet<ElementId>(existing.GetCategories());

            List<string> missing = new List<string>();
            foreach (Category category in categories)
            {
                if (category != null && !currentCategoryIds.Contains(category.Id))
                {
                    missing.Add(category.Name);
                }
            }

            return missing
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }

        public AssignExistingResult Execute(
            Document doc,
            ElementId parameterId,
            string parameterName,
            AssemblyInstance assembly,
            ICollection<ElementId> elementIds,
            int nestedCount,
            bool parameterExisted)
        {
            AssignExistingResult result = new AssignExistingResult
            {
                ParameterName = parameterName,
                AssemblyName = assembly.Name,
                TotalElementCount = elementIds.Count,
                NestedCount = nestedCount
            };

            HashSet<Category> categories = CollectCategories(doc, elementIds);

            result.AddedParameterCategories = parameterExisted
                ? GetMissingParameterCategoryNames(doc, parameterId, categories)
                : categories.Select(c => c.Name).OrderBy(n => n).ToList();

            if (result.AddedParameterCategories.Count > 0)
            {
                _parameterService.AddMissingCategories(doc, parameterId, categories);
                doc.Regenerate();
            }

            foreach (ElementId elementId in elementIds)
            {
                Element element = doc.GetElement(elementId);
                if (element == null)
                {
                    continue;
                }

                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter == null)
                {
                    result.SkippedNoParameterCount++;
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    result.SkippedReadOnlyCount++;
                    continue;
                }

                parameter.Set(assembly.Name);
                result.WrittenCount++;
            }

            UpdateAssemblyFilter(doc, parameterId, assembly.Name, categories, result);

            return result;
        }

        private List<string> GetMissingParameterCategoryNames(Document doc, ElementId parameterId, ICollection<Category> categories)
        {
            ParameterValidationResult validation = _parameterService.ValidateParameterCategories(doc, parameterId, categories);
            return validation.MissingCategories;
        }

        private void UpdateAssemblyFilter(Document doc, ElementId parameterId, string assemblyName, HashSet<Category> categories, AssignExistingResult result)
        {
            string filterName = assemblyName + FilterSuffix;
            ParameterFilterElement existingFilter = _filterService.FindFilterByName(doc, filterName);

            List<Category> targetCategories;

            if (existingFilter == null)
            {
                result.FilterCreated = true;
                result.AddedFilterCategories = categories
                    .Select(c => c.Name)
                    .OrderBy(n => n)
                    .ToList();

                targetCategories = categories.ToList();
            }
            else
            {
                result.AddedFilterCategories = GetMissingFilterCategoryNames(doc, assemblyName, categories);

                List<Category> existingCategories = existingFilter.GetCategories()
                    .Select(id => Category.GetCategory(doc, id))
                    .Where(c => c != null)
                    .ToList();

                targetCategories = existingCategories;
                foreach (Category category in categories)
                {
                    if (category != null && targetCategories.All(c => c.Id != category.Id))
                    {
                        targetCategories.Add(category);
                    }
                }
            }

            if (targetCategories.Count == 0)
            {
                Logger.Warn($"No categories for assembly filter '{filterName}', filter not created.");
                return;
            }

            (ParameterFilterElement Filter, bool Recreated) ensured = _filterService.EnsureAssemblyFilter(doc, parameterId, assemblyName, targetCategories);

            if (ensured.Recreated && existingFilter != null)
            {
                result.FilterRecreated = true;
            }
        }
    }
}
