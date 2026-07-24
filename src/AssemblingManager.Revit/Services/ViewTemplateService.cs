using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class ViewTemplateItem
    {
        public string Name { get; set; }
        public int? Id { get; set; }
    }

    public class ViewTemplateService
    {
        private const string NoneOptionName = "— без шаблона —";

        public List<ViewTemplateItem> GetPlanTemplates(Document doc)
        {
            return GetTemplates(doc, ViewType.FloorPlan);
        }

        public List<ViewTemplateItem> GetSectionTemplates(Document doc)
        {
            // Revit groups section, elevation, and detail/callout templates under one category.
            return GetTemplates(doc, ViewType.Section, ViewType.Elevation, ViewType.Detail);
        }

        public List<ViewTemplateItem> GetView3DTemplates(Document doc)
        {
            return GetTemplates(doc, ViewType.ThreeD);
        }

        private List<ViewTemplateItem> GetTemplates(Document doc, params ViewType[] viewTypes)
        {
            HashSet<ViewType> allowedTypes = new HashSet<ViewType>(viewTypes);

            List<ViewTemplateItem> templates = new List<ViewTemplateItem>
            {
                new ViewTemplateItem { Name = NoneOptionName, Id = null }
            };

            List<View> matchingTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && allowedTypes.Contains(v.ViewType))
                .OrderBy(v => v.Name)
                .ToList();

            foreach (View template in matchingTemplates)
            {
#pragma warning disable CS0618
                int id = template.Id.IntegerValue;
#pragma warning restore CS0618
                templates.Add(new ViewTemplateItem
                {
                    Name = template.Name,
                    Id = id
                });
            }

            return templates;
        }
        public bool IsTemplateLockingFilters(Document doc, int templateId)
        {
#pragma warning disable CS0618
            ElementId templateElementId = new ElementId(templateId);
#pragma warning restore CS0618
            View template = doc.GetElement(templateElementId) as View;
            if (template == null || !template.IsTemplate)
            {
                return false;
            }

            ICollection<ElementId> controlledParameterIds = template.GetTemplateParameterIds();
            ICollection<ElementId> nonControlledParameterIds = template.GetNonControlledTemplateParameterIds();

            foreach (ElementId parameterId in controlledParameterIds)
            {
                if (nonControlledParameterIds.Contains(parameterId))
                {
                    continue;
                }

                if (IsVisGraphicsFiltersParameter(parameterId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsVisGraphicsFiltersParameter(ElementId parameterId)
        {
#pragma warning disable CS0618
            int value = parameterId.IntegerValue;
#pragma warning restore CS0618
            BuiltInParameter bip = (BuiltInParameter)value;
            return bip.ToString() == "VIS_GRAPHICS_FILTERS";
        }
    }
}
