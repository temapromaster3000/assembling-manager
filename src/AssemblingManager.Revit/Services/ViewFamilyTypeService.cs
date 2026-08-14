using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class ViewFamilyTypeItem
    {
        public string Name { get; set; }
        public int? Id { get; set; }
    }

    public class ViewFamilyTypeService
    {
        public List<ViewFamilyTypeItem> GetPlanTypes(Document doc)
        {
            return GetTypes(doc, ViewFamily.FloorPlan);
        }

        public List<ViewFamilyTypeItem> GetSectionTypes(Document doc)
        {
            return GetTypes(doc, ViewFamily.Section);
        }

        public List<ViewFamilyTypeItem> GetView3DTypes(Document doc)
        {
            return GetTypes(doc, ViewFamily.ThreeDimensional);
        }

        private List<ViewFamilyTypeItem> GetTypes(Document doc, ViewFamily viewFamily)
        {
            List<ViewFamilyType> types = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(vft => vft.ViewFamily == viewFamily)
                .OrderBy(vft => vft.Name)
                .ToList();

            List<ViewFamilyTypeItem> result = new List<ViewFamilyTypeItem>();

            foreach (ViewFamilyType type in types)
            {
#pragma warning disable CS0618
                int id = type.Id.IntegerValue;
#pragma warning restore CS0618
                result.Add(new ViewFamilyTypeItem
                {
                    Name = type.Name,
                    Id = id
                });
            }

            return result;
        }
    }
}
