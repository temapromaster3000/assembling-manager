using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class ParameterService
    {
        private const string ParameterName = "AssemblyParameter";

        public ElementId GetOrCreateParameter(Document doc, Application app, ICollection<Category> categories)
        {
            ElementId parameterId = GetExistingParameterId(doc, ParameterName);

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
                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(ParameterName, ParameterType.Text);
#else
                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(ParameterName, SpecTypeId.String.Text);
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

        public void SetParameterValue(Document doc, ElementId parameterId, ICollection<ElementId> elementIds, string value)
        {
            foreach (ElementId elementId in elementIds)
            {
                Element element = doc.GetElement(elementId);
                if (element == null) continue;

                Parameter parameter = element.LookupParameter(ParameterName);
                if (parameter == null || parameter.IsReadOnly) continue;

                parameter.Set(value);
            }
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
