using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class ViewService
    {
        private const string PlanSuffix = "_План";
        private const string Section1Suffix = "_Разрез_1";
        private const string Section2Suffix = "_Разрез_2";
        private const string View3DSuffix = "_3D";

        public void DeleteExistingViews(Document doc, string assemblyName)
        {
            string[] names =
            {
                assemblyName + PlanSuffix,
                assemblyName + Section1Suffix,
                assemblyName + Section2Suffix,
                assemblyName + View3DSuffix
            };

            List<ElementId> viewsToDelete = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => names.Contains(v.Name))
                .Select(v => v.Id)
                .ToList();

            if (viewsToDelete.Count > 0)
            {
                doc.Delete(viewsToDelete);
            }
        }

        public ViewPlan CreatePlanView(Document doc, string assemblyName, BoundingBoxXYZ bbox, ElementId levelId)
        {
            ElementId viewFamilyTypeId = GetViewFamilyTypeId(doc, ViewFamily.FloorPlan);

            ViewPlan viewPlan = ViewPlan.Create(doc, viewFamilyTypeId, levelId);
            viewPlan.Name = assemblyName + PlanSuffix;
            viewPlan.CropBoxActive = true;
            viewPlan.CropBoxVisible = true;

            BoundingBoxXYZ cropBox = new BoundingBoxXYZ();
            cropBox.Min = new XYZ(bbox.Min.X, bbox.Min.Y, -1000);
            cropBox.Max = new XYZ(bbox.Max.X, bbox.Max.Y, 1000);
            viewPlan.CropBox = cropBox;

            return viewPlan;
        }

        public ViewSection CreateSectionView1(Document doc, string assemblyName, BoundingBoxXYZ bbox)
        {
            ElementId viewFamilyTypeId = GetViewFamilyTypeId(doc, ViewFamily.Section);

            XYZ center = (bbox.Min + bbox.Max) / 2;
            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            Transform transform = Transform.Identity;
            transform.Origin = center;
            transform.BasisX = XYZ.BasisY;
            transform.BasisY = XYZ.BasisZ;
            transform.BasisZ = XYZ.BasisX;

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;
            sectionBox.Min = new XYZ(-dy / 2 - 0.5, -0.5, -dx / 2 - 0.5);
            sectionBox.Max = new XYZ(dy / 2 + 0.5, dz + 0.5, dx / 2 + 0.5);

            ViewSection viewSection = ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox);
            viewSection.Name = assemblyName + Section1Suffix;

            return viewSection;
        }

        public ViewSection CreateSectionView2(Document doc, string assemblyName, BoundingBoxXYZ bbox)
        {
            ElementId viewFamilyTypeId = GetViewFamilyTypeId(doc, ViewFamily.Section);

            XYZ center = (bbox.Min + bbox.Max) / 2;
            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            Transform transform = Transform.Identity;
            transform.Origin = center;
            transform.BasisX = -XYZ.BasisX;
            transform.BasisY = XYZ.BasisZ;
            transform.BasisZ = XYZ.BasisY;

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;
            sectionBox.Min = new XYZ(-dx / 2 - 0.5, -0.5, -dy / 2 - 0.5);
            sectionBox.Max = new XYZ(dx / 2 + 0.5, dz + 0.5, dy / 2 + 0.5);

            ViewSection viewSection = ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox);
            viewSection.Name = assemblyName + Section2Suffix;

            return viewSection;
        }

        public View3D Create3DView(Document doc, string assemblyName, BoundingBoxXYZ bbox)
        {
            ElementId viewFamilyTypeId = GetViewFamilyTypeId(doc, ViewFamily.ThreeDimensional);

            View3D view3D = View3D.CreateIsometric(doc, viewFamilyTypeId);
            view3D.Name = assemblyName + View3DSuffix;
            view3D.SetSectionBox(bbox);

            return view3D;
        }

        private ElementId GetViewFamilyTypeId(Document doc, ViewFamily viewFamily)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(vft => vft.ViewFamily == viewFamily)
                .Select(vft => vft.Id)
                .FirstOrDefault();
        }
    }
}
