using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class AssignExistingService
    {
        private readonly ParameterService _parameterService = new ParameterService();

        public AssignExistingResult Execute(
            Document doc,
            ElementId parameterId,
            string parameterName,
            AssemblyInstance assembly,
            ICollection<ElementId> elementIds,
            HashSet<Category> assemblyCategories,
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

            result.AddedParameterCategories = parameterExisted
                ? _parameterService.ValidateParameterCategories(doc, parameterId, assemblyCategories).MissingCategories
                : assemblyCategories.Select(c => c.Name).OrderBy(n => n).ToList();

            if (result.AddedParameterCategories.Count > 0)
            {
                _parameterService.AddMissingCategories(doc, parameterId, assemblyCategories);
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

            return result;
        }
    }
}
