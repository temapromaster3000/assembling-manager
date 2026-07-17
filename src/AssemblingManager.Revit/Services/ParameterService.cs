using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class ParameterService
    {
        private const string AssemblyParameterName = "AssemblyParameter";
        private const string GroupingParameterName = "ADSK_Группирование";

        public ElementId GetOrCreateParameter(Document doc, Application app, ICollection<Category> categories)
        {
            ElementId parameterId = GetExistingParameterId(doc, AssemblyParameterName);

            if (parameterId != null)
            {
                return parameterId;
            }

            string tempSharedParamsFile = Path.GetTempFileName();
            string originalSharedParamsFile = app.SharedParametersFilename;

            try
            {
                File.WriteAllText(tempSharedParamsFile, "*SECTION1\n");
                app.SharedParametersFilename = tempSharedParamsFile;

                DefinitionFile definitionFile = app.OpenSharedParameterFile();
                DefinitionGroup group = definitionFile.Groups.Create("AssemblingManager");

#if REVIT2021_OR_GREATER && !REVIT2022_OR_GREATER
                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(AssemblyParameterName, ParameterType.Text);
#else
                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(AssemblyParameterName, SpecTypeId.String.Text);
#endif
                ExternalDefinition definition = group.Definitions.Create(options) as ExternalDefinition;

                CategorySet categorySet = app.Create.NewCategorySet();
                foreach (Category category in categories)
                {
                    if (category != null && category.AllowsBoundParameters)
                    {
                        categorySet.Insert(category);
                    }
                }

                if (categorySet.IsEmpty)
                {
                    throw new InvalidOperationException("Не найдено ни одной категории для привязки параметра.");
                }

                InstanceBinding binding = app.Create.NewInstanceBinding(categorySet);
#if REVIT2024_OR_GREATER
                doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Text);
#else
                doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_TEXT);
#endif

                Guid parameterGuid = definition.GUID;
                SharedParameterElement sharedParameterElement = SharedParameterElement.Lookup(doc, parameterGuid);

                return sharedParameterElement?.Id;
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalSharedParamsFile))
                {
                    app.SharedParametersFilename = originalSharedParamsFile;
                }

                try
                {
                    File.Delete(tempSharedParamsFile);
                }
                catch
                {
                }
            }
        }

        public ElementId GetParameterByName(Document doc, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return null;
            }

            SharedParameterElement sharedParameterElement = new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .FirstOrDefault(sp => sp.Name == parameterName);

            return sharedParameterElement?.Id;
        }

        public ParameterValidationResult ValidateParameterCategories(Document doc, ElementId parameterId, IEnumerable<Category> categories)
        {
            ParameterValidationResult result = new ParameterValidationResult
            {
                IsValid = true,
                AllCategoriesBound = true,
                MissingCategories = new List<string>()
            };

            if (parameterId == null || categories == null)
            {
                return result;
            }

            SharedParameterElement parameterElement = doc.GetElement(parameterId) as SharedParameterElement;
            if (parameterElement == null)
            {
                result.IsValid = false;
                result.AllCategoriesBound = false;
                return result;
            }

            Definition definition = FindDefinition(doc, parameterElement.Name);
            if (definition == null)
            {
                result.IsValid = false;
                result.AllCategoriesBound = false;
                return result;
            }

            Binding binding = doc.ParameterBindings.get_Item(definition);
            if (binding == null)
            {
                result.IsValid = false;
                result.AllCategoriesBound = false;
                return result;
            }

            if (!(binding is InstanceBinding))
            {
                result.IsValid = false;
                result.IsTypeBinding = true;
                result.AllCategoriesBound = false;
                return result;
            }

            CategorySet boundCategories = GetBindingCategories(binding);
            if (boundCategories == null)
            {
                result.IsValid = false;
                result.AllCategoriesBound = false;
                return result;
            }

            HashSet<ElementId> boundCategoryIds = new HashSet<ElementId>();

            foreach (Category category in boundCategories)
            {
                if (category != null)
                {
                    boundCategoryIds.Add(category.Id);
                }
            }

            foreach (Category category in categories)
            {
                if (category == null)
                {
                    continue;
                }

                if (!boundCategoryIds.Contains(category.Id))
                {
                    result.AllCategoriesBound = false;
                    result.MissingCategories.Add(category.Name);
                }
            }

            result.MissingCategories = result.MissingCategories
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            result.IsValid = true;
            return result;
        }

        public int CountExistingValues(Document doc, string parameterName, IEnumerable<ElementId> elementIds)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || elementIds == null)
            {
                return 0;
            }

            int count = 0;

            foreach (ElementId elementId in elementIds)
            {
                Element element = doc.GetElement(elementId);
                if (element == null)
                {
                    continue;
                }

                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter == null || !parameter.HasValue)
                {
                    continue;
                }

                string value = parameter.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    count++;
                }
            }

            return count;
        }

        public void AddMissingCategories(Document doc, ElementId parameterId, ICollection<Category> categories)
        {
            if (parameterId == null)
            {
                throw new ArgumentNullException(nameof(parameterId));
            }

            if (categories == null || categories.Count == 0)
            {
                return;
            }

            SharedParameterElement parameterElement = doc.GetElement(parameterId) as SharedParameterElement;
            if (parameterElement == null)
            {
                throw new InvalidOperationException($"Параметр с Id {parameterId} не найден.");
            }

            Definition definition = FindDefinition(doc, parameterElement.Name);
            if (definition == null)
            {
                throw new InvalidOperationException($"Привязка параметра '{parameterElement.Name}' не найдена.");
            }

            Binding binding = doc.ParameterBindings.get_Item(definition);
            if (binding == null)
            {
                throw new InvalidOperationException($"Привязка параметра '{parameterElement.Name}' не найдена.");
            }

            InstanceBinding instanceBinding = binding as InstanceBinding;
            if (instanceBinding == null)
            {
                throw new InvalidOperationException($"Параметр '{parameterElement.Name}' должен быть привязан к экземплярам.");
            }

            CategorySet boundCategories = instanceBinding.Categories;
            if (boundCategories == null)
            {
                boundCategories = doc.Application.Create.NewCategorySet();
            }

            bool added = false;
            foreach (Category category in categories)
            {
                if (category != null && category.AllowsBoundParameters && !boundCategories.Contains(category))
                {
                    boundCategories.Insert(category);
                    added = true;
                }
            }

            if (added)
            {
                InstanceBinding newBinding = doc.Application.Create.NewInstanceBinding(boundCategories);
                doc.ParameterBindings.ReInsert(definition, newBinding);
            }
        }

        public void SetParameterValue(Document doc, ElementId parameterId, ICollection<ElementId> elementIds, string value)
        {
            if (parameterId == null)
            {
                throw new ArgumentNullException(nameof(parameterId));
            }

            SharedParameterElement parameterElement = doc.GetElement(parameterId) as SharedParameterElement;
            if (parameterElement == null)
            {
                throw new InvalidOperationException($"Параметр с Id {parameterId} не найден.");
            }

            SetParameterValue(doc, parameterElement.Name, elementIds, value);
        }

        public void SetParameterValue(Document doc, string parameterName, ICollection<ElementId> elementIds, string value)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                throw new ArgumentException("Имя параметра не может быть пустым.", nameof(parameterName));
            }

            foreach (ElementId elementId in elementIds)
            {
                Element element = doc.GetElement(elementId);
                if (element == null) continue;

                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter == null || parameter.IsReadOnly) continue;

                parameter.Set(value);
            }
        }

        private CategorySet GetBindingCategories(Binding binding)
        {
            if (binding == null)
            {
                return null;
            }

            InstanceBinding instanceBinding = binding as InstanceBinding;
            if (instanceBinding != null)
            {
                return instanceBinding.Categories;
            }

            TypeBinding typeBinding = binding as TypeBinding;
            if (typeBinding != null)
            {
                return typeBinding.Categories;
            }

            return null;
        }

        private bool IsTextParameter(Definition definition)
        {
            if (definition == null)
            {
                return false;
            }

            ExternalDefinition externalDefinition = definition as ExternalDefinition;
            if (externalDefinition != null)
            {
#if REVIT2021_OR_GREATER && !REVIT2022_OR_GREATER
                return externalDefinition.ParameterType == ParameterType.Text;
#else
                return externalDefinition.GetDataType().Equals(SpecTypeId.String.Text);
#endif
            }

            InternalDefinition internalDefinition = definition as InternalDefinition;
            if (internalDefinition != null)
            {
#if REVIT2021_OR_GREATER && !REVIT2022_OR_GREATER
                return internalDefinition.ParameterType == ParameterType.Text;
#else
                return internalDefinition.GetDataType().Equals(SpecTypeId.String.Text);
#endif
            }

            return false;
        }

        private Definition FindDefinition(Document doc, string parameterName)
        {
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();

            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key as Definition;
                if (definition != null && definition.Name == parameterName)
                {
                    return definition;
                }
            }

            return null;
        }

        private ElementId GetExistingParameterId(Document doc, string parameterName)
        {
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key as Definition;
                if (definition != null && definition.Name == parameterName)
                {
                    ExternalDefinition externalDefinition = definition as ExternalDefinition;
                    if (externalDefinition != null)
                    {
                        SharedParameterElement sharedParameterElement = SharedParameterElement.Lookup(doc, externalDefinition.GUID);
                        return sharedParameterElement?.Id;
                    }
                }
            }

            SharedParameterElement byName = new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .FirstOrDefault(sp => sp.Name == parameterName);

            return byName?.Id;
        }
    }
}
