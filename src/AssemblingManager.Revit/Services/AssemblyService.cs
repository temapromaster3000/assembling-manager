using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class AssemblyService
    {
        public ICollection<ElementId> CollectAssemblyElements(Document doc, AssemblyInstance assembly)
        {
            HashSet<ElementId> result = new HashSet<ElementId>();

            foreach (ElementId memberId in assembly.GetMemberIds())
            {
                result.Add(memberId);
                Element element = doc.GetElement(memberId);
                if (element is FamilyInstance familyInstance)
                {
                    CollectNestedSharedFamilies(doc, familyInstance, result);
                }
            }

            return result;
        }

        private void CollectNestedSharedFamilies(Document doc, FamilyInstance parent, HashSet<ElementId> result)
        {
            foreach (ElementId subId in parent.GetSubComponentIds())
            {
                if (result.Add(subId))
                {
                    Element subElement = doc.GetElement(subId);
                    if (subElement is FamilyInstance subFamilyInstance)
                    {
                        CollectNestedSharedFamilies(doc, subFamilyInstance, result);
                    }
                }
            }
        }

        public BoundingBoxXYZ GetElementsBoundingBox(Document doc, ICollection<ElementId> elementIds, double offset = 0.5)
        {
            BoundingBoxXYZ result = null;

            foreach (ElementId id in elementIds)
            {
                Element element = doc.GetElement(id);
                if (element == null) continue;

                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null) continue;

                if (result == null)
                {
                    result = new BoundingBoxXYZ
                    {
                        Min = bbox.Min,
                        Max = bbox.Max
                    };
                }
                else
                {
                    result.Min = new XYZ(
                        Math.Min(result.Min.X, bbox.Min.X),
                        Math.Min(result.Min.Y, bbox.Min.Y),
                        Math.Min(result.Min.Z, bbox.Min.Z));

                    result.Max = new XYZ(
                        Math.Max(result.Max.X, bbox.Max.X),
                        Math.Max(result.Max.Y, bbox.Max.Y),
                        Math.Max(result.Max.Z, bbox.Max.Z));
                }
            }

            if (result != null)
            {
                result.Min = new XYZ(result.Min.X - offset, result.Min.Y - offset, result.Min.Z - offset);
                result.Max = new XYZ(result.Max.X + offset, result.Max.Y + offset, result.Max.Z + offset);
            }

            return result;
        }

        public ElementId GetNearestLevelId(Document doc, double elevation)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(l => l.Elevation <= elevation)
                .OrderByDescending(l => l.Elevation)
                .Select(l => l.Id)
                .FirstOrDefault();
        }
    }
}
