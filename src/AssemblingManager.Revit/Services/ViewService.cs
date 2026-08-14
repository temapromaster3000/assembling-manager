using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class ViewService
    {
        public const string PlanSuffix = "_План";
        public const string FrontViewSuffix = "_Вид спереди";
        public const string BackViewSuffix = "_Вид сзади";
        public const string RightViewSuffix = "_Вид справа";
        public const string LeftViewSuffix = "_Вид слева";
        public const string View3DSuffix = "_3D";

        private readonly IReadOnlyList<ViewTypeInfo> _viewTypes = new List<ViewTypeInfo>
        {
            new ViewTypeInfo(ViewType.Plan, PlanSuffix, "План"),
            new ViewTypeInfo(ViewType.FrontView, FrontViewSuffix, "Вид спереди"),
            new ViewTypeInfo(ViewType.BackView, BackViewSuffix, "Вид сзади"),
            new ViewTypeInfo(ViewType.RightView, RightViewSuffix, "Вид справа"),
            new ViewTypeInfo(ViewType.LeftView, LeftViewSuffix, "Вид слева"),
            new ViewTypeInfo(ViewType.View3D, View3DSuffix, "3D вид")
        };

        public void DeleteExistingViews(Document doc, string assemblyName)
        {
            string[] names =
            {
                assemblyName + PlanSuffix,
                assemblyName + FrontViewSuffix,
                assemblyName + BackViewSuffix,
                assemblyName + RightViewSuffix,
                assemblyName + LeftViewSuffix,
                assemblyName + View3DSuffix
            };

            DeleteViewsByNames(doc, names);
        }

        public void DeleteViewsByNames(Document doc, IEnumerable<string> viewNames)
        {
            HashSet<string> names = new HashSet<string>(viewNames);

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

        public List<ViewConflictItem> FindExistingViewConflicts(Document doc, IEnumerable<AssemblyInstance> assemblies, ViewCreationOptions options)
        {
            List<ViewConflictItem> conflicts = new List<ViewConflictItem>();

            HashSet<string> existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name));

            foreach (AssemblyInstance assembly in assemblies)
            {
                foreach (ViewTypeInfo viewType in _viewTypes)
                {
                    if (!IsViewTypeSelected(options, viewType.Type))
                    {
                        continue;
                    }

                    string viewName = assembly.Name + viewType.Suffix;
                    if (existingNames.Contains(viewName))
                    {
                        conflicts.Add(new ViewConflictItem
                        {
                            AssemblyName = assembly.Name,
                            ViewName = viewName,
                            ViewTypeDisplayName = viewType.DisplayName,
                            Replace = false
                        });
                    }
                }
            }

            return conflicts;
        }

        private bool IsViewTypeSelected(ViewCreationOptions options, ViewType viewType)
        {
            switch (viewType)
            {
                case ViewType.Plan: return options.CreatePlan;
                case ViewType.FrontView: return options.CreateFrontView;
                case ViewType.BackView: return options.CreateBackView;
                case ViewType.RightView: return options.CreateRightView;
                case ViewType.LeftView: return options.CreateLeftView;
                case ViewType.View3D: return options.Create3D;
                default: return false;
            }
        }

        private class ViewTypeInfo
        {
            public ViewType Type { get; }
            public string Suffix { get; }
            public string DisplayName { get; }

            public ViewTypeInfo(ViewType type, string suffix, string displayName)
            {
                Type = type;
                Suffix = suffix;
                DisplayName = displayName;
            }
        }

        private enum ViewType
        {
            Plan,
            FrontView,
            BackView,
            RightView,
            LeftView,
            View3D
        }

        public View GetViewByName(Document doc, string viewName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.Name == viewName);
        }

        public List<View> GetExistingAssemblyViews(Document doc, string assemblyName)
        {
            string[] names =
            {
                assemblyName + PlanSuffix,
                assemblyName + FrontViewSuffix,
                assemblyName + BackViewSuffix,
                assemblyName + RightViewSuffix,
                assemblyName + LeftViewSuffix,
                assemblyName + View3DSuffix
            };

            HashSet<string> nameSet = new HashSet<string>(names);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => nameSet.Contains(v.Name))
                .ToList();
        }

        private const double MillimetersToFeet = 1.0 / 304.8;

        public ViewPlan CreatePlanView(Document doc, string assemblyName, BoundingBoxXYZ bbox, ElementId levelId, int? viewFamilyTypeId = null)
        {
            ElementId selectedViewFamilyTypeId = ResolveViewFamilyTypeId(doc, ViewFamily.FloorPlan, viewFamilyTypeId);

            ViewPlan viewPlan = ViewPlan.Create(doc, selectedViewFamilyTypeId, levelId);
            ApplyPlanViewGeometry(doc, viewPlan, assemblyName, bbox, levelId);

            return viewPlan;
        }

        public ViewSection CreateFrontView(Document doc, string assemblyName, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            ElementId selectedViewFamilyTypeId = ResolveViewFamilyTypeId(doc, ViewFamily.Section, viewFamilyTypeId);

            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            return CreateSectionView(
                doc,
                selectedViewFamilyTypeId,
                assemblyName,
                bbox,
                FrontViewSuffix,
                -XYZ.BasisX,
                XYZ.BasisZ,
                XYZ.BasisY,
                dx,
                dz,
                dy);
        }

        public ViewSection CreateBackView(Document doc, string assemblyName, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            ElementId selectedViewFamilyTypeId = ResolveViewFamilyTypeId(doc, ViewFamily.Section, viewFamilyTypeId);

            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            return CreateSectionView(
                doc,
                selectedViewFamilyTypeId,
                assemblyName,
                bbox,
                BackViewSuffix,
                XYZ.BasisX,
                XYZ.BasisZ,
                -XYZ.BasisY,
                dx,
                dz,
                dy);
        }

        public ViewSection CreateRightView(Document doc, string assemblyName, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            ElementId selectedViewFamilyTypeId = ResolveViewFamilyTypeId(doc, ViewFamily.Section, viewFamilyTypeId);

            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            return CreateSectionView(
                doc,
                selectedViewFamilyTypeId,
                assemblyName,
                bbox,
                RightViewSuffix,
                -XYZ.BasisY,
                XYZ.BasisZ,
                -XYZ.BasisX,
                dy,
                dz,
                dx);
        }

        public ViewSection CreateLeftView(Document doc, string assemblyName, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            ElementId selectedViewFamilyTypeId = ResolveViewFamilyTypeId(doc, ViewFamily.Section, viewFamilyTypeId);

            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            return CreateSectionView(
                doc,
                selectedViewFamilyTypeId,
                assemblyName,
                bbox,
                LeftViewSuffix,
                XYZ.BasisY,
                XYZ.BasisZ,
                XYZ.BasisX,
                dy,
                dz,
                dx);
        }

        public View3D Create3DView(Document doc, string assemblyName, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            ElementId selectedViewFamilyTypeId = ResolveViewFamilyTypeId(doc, ViewFamily.ThreeDimensional, viewFamilyTypeId);

            View3D view3D = View3D.CreateIsometric(doc, selectedViewFamilyTypeId);
            Apply3DViewGeometry(view3D, assemblyName, bbox);

            return view3D;
        }

        private void ApplyPlanViewGeometry(Document doc, ViewPlan viewPlan, string assemblyName, BoundingBoxXYZ bbox, ElementId levelId)
        {
            viewPlan.Name = assemblyName + PlanSuffix;
            viewPlan.CropBoxActive = true;
            viewPlan.CropBoxVisible = true;

            double minZMm = bbox.Min.Z / MillimetersToFeet;
            double maxZMm = bbox.Max.Z / MillimetersToFeet;

            double roundedMinZMm = RoundToHundred(minZMm, false);
            double roundedMaxZMm = RoundToHundred(maxZMm, true);

            double cropOffsetMm = 500;
            BoundingBoxXYZ cropBox = new BoundingBoxXYZ();
            cropBox.Min = new XYZ(
                bbox.Min.X - cropOffsetMm * MillimetersToFeet,
                bbox.Min.Y - cropOffsetMm * MillimetersToFeet,
                (roundedMinZMm - cropOffsetMm) * MillimetersToFeet);
            cropBox.Max = new XYZ(
                bbox.Max.X + cropOffsetMm * MillimetersToFeet,
                bbox.Max.Y + cropOffsetMm * MillimetersToFeet,
                (roundedMaxZMm + cropOffsetMm) * MillimetersToFeet);
            viewPlan.CropBox = cropBox;

            Level level = doc.GetElement(levelId) as Level;
            double levelElevationMm = (level?.Elevation ?? 0.0) / MillimetersToFeet;

            double viewRangeOffsetMm = 5000;
            double cutPlaneElevationMm = (roundedMinZMm + roundedMaxZMm) / 2.0;
            PlanViewRange planViewRange = viewPlan.GetViewRange();

            planViewRange.SetLevelId(PlanViewPlane.TopClipPlane, levelId);
            planViewRange.SetLevelId(PlanViewPlane.CutPlane, levelId);
            planViewRange.SetLevelId(PlanViewPlane.BottomClipPlane, levelId);
            planViewRange.SetLevelId(PlanViewPlane.ViewDepthPlane, levelId);

            planViewRange.SetOffset(PlanViewPlane.TopClipPlane, (roundedMaxZMm + viewRangeOffsetMm - levelElevationMm) * MillimetersToFeet);
            planViewRange.SetOffset(PlanViewPlane.CutPlane, (cutPlaneElevationMm - levelElevationMm) * MillimetersToFeet);
            planViewRange.SetOffset(PlanViewPlane.BottomClipPlane, (roundedMinZMm - viewRangeOffsetMm - levelElevationMm) * MillimetersToFeet);
            planViewRange.SetOffset(PlanViewPlane.ViewDepthPlane, (roundedMinZMm - viewRangeOffsetMm - levelElevationMm) * MillimetersToFeet);

            viewPlan.SetViewRange(planViewRange);
        }

        private static double RoundToHundred(double valueMm, bool roundUp)
        {
            const double factor = 100.0;
            if (roundUp)
            {
                return Math.Ceiling(valueMm / factor) * factor;
            }
            return Math.Floor(valueMm / factor) * factor;
        }

        private const double SectionViewOffsetMm = 500.0;
        private const double MinimumViewDimensionMm = 10.0;

        private static double GetSectionBoxOffset()
        {
            return SectionViewOffsetMm * MillimetersToFeet;
        }

        private static double EnsureMinimumSize(double sizeInFeet)
        {
            double sizeInMm = sizeInFeet / MillimetersToFeet;
            double minSizeInMm = Math.Max(sizeInMm, MinimumViewDimensionMm);
            return minSizeInMm * MillimetersToFeet;
        }

        private static ViewSection CreateSectionView(
            Document doc,
            ElementId viewFamilyTypeId,
            string assemblyName,
            BoundingBoxXYZ bbox,
            string suffix,
            XYZ basisX,
            XYZ basisY,
            XYZ basisZ,
            double width,
            double height,
            double depth)
        {
            XYZ center = (bbox.Min + bbox.Max) / 2;

            Transform transform = Transform.Identity;
            transform.Origin = center;
            transform.BasisX = basisX;
            transform.BasisY = basisY;
            transform.BasisZ = basisZ;

            double offset = GetSectionBoxOffset();

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;
            sectionBox.Min = new XYZ(-width / 2 - offset, -height / 2 - offset, -depth / 2 - offset);
            sectionBox.Max = new XYZ( width / 2 + offset,  height / 2 + offset,  depth / 2 + offset);

            ViewSection viewSection = ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox);
            viewSection.Name = assemblyName + suffix;

            return viewSection;
        }

        private void Apply3DViewGeometry(View3D view3D, string assemblyName, BoundingBoxXYZ bbox)
        {
            view3D.Name = assemblyName + View3DSuffix;
            view3D.SetSectionBox(bbox);
        }

        public ViewPlan DuplicatePlanView(Document doc, ViewPlan source, string assemblyName, BoundingBoxXYZ bbox, ElementId levelId, int? viewFamilyTypeId = null)
        {
            if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            {
                throw new InvalidOperationException($"Plan view '{source.Name}' cannot be duplicated.");
            }

            ElementId newId = source.Duplicate(ViewDuplicateOption.Duplicate);
            ViewPlan viewPlan = doc.GetElement(newId) as ViewPlan;
            if (viewPlan == null)
            {
                throw new InvalidOperationException("Duplicated plan view is not valid.");
            }

            ApplyPlanViewGeometry(doc, viewPlan, assemblyName, bbox, levelId);

            if (viewFamilyTypeId.HasValue)
            {
                ChangeViewFamilyType(viewPlan, ViewFamily.FloorPlan, viewFamilyTypeId.Value);
            }

            return viewPlan;
        }

        public View3D Duplicate3DView(Document doc, View3D source, string assemblyName, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            {
                throw new InvalidOperationException($"3D view '{source.Name}' cannot be duplicated.");
            }

            ElementId newId = source.Duplicate(ViewDuplicateOption.Duplicate);
            View3D view3D = doc.GetElement(newId) as View3D;
            if (view3D == null)
            {
                throw new InvalidOperationException("Duplicated 3D view is not valid.");
            }

            Apply3DViewGeometry(view3D, assemblyName, bbox);

            if (viewFamilyTypeId.HasValue)
            {
                ChangeViewFamilyType(view3D, ViewFamily.ThreeDimensional, viewFamilyTypeId.Value);
            }

            return view3D;
        }

        public ViewSection DuplicateSectionView(Document doc, ViewSection source, string assemblyName, string suffix, BoundingBoxXYZ bbox, int? viewFamilyTypeId = null)
        {
            if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            {
                throw new InvalidOperationException($"Section view '{source.Name}' cannot be duplicated.");
            }

            ElementId newId = source.Duplicate(ViewDuplicateOption.Duplicate);
            ViewSection viewSection = doc.GetElement(newId) as ViewSection;
            if (viewSection == null)
            {
                throw new InvalidOperationException("Duplicated section view is not valid.");
            }

            BoundingBoxXYZ cropBox = CalculateSectionCropBoxFromSourceTransform(source.CropBox.Transform, bbox);
            viewSection.CropBox = cropBox;
            viewSection.Name = assemblyName + suffix;

            if (viewFamilyTypeId.HasValue)
            {
                ChangeViewFamilyType(viewSection, ViewFamily.Section, viewFamilyTypeId.Value);
            }

            return viewSection;
        }

        private void ChangeViewFamilyType(View view, ViewFamily expectedFamily, int viewFamilyTypeId)
        {
            try
            {
                ElementId selectedTypeId = ResolveViewFamilyTypeId(view.Document, expectedFamily, viewFamilyTypeId);
                if (selectedTypeId != null && view.GetTypeId() != selectedTypeId)
                {
                    view.ChangeTypeId(selectedTypeId);
                    Logger.Debug($"Changed type of view '{view.Name}' to {selectedTypeId}.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not change type of view '{view.Name}': {ex.Message}");
            }
        }

        private BoundingBoxXYZ CalculateSectionCropBoxFromSourceTransform(Transform sourceTransform, BoundingBoxXYZ targetBBox)
        {
            XYZ targetCenter = (targetBBox.Min + targetBBox.Max) / 2;

            Transform newTransform = Transform.Identity;
            newTransform.Origin = targetCenter;
            newTransform.BasisX = sourceTransform.BasisX;
            newTransform.BasisY = sourceTransform.BasisY;
            newTransform.BasisZ = sourceTransform.BasisZ;

            XYZ localMin = newTransform.Inverse.OfPoint(targetBBox.Min);
            XYZ localMax = newTransform.Inverse.OfPoint(targetBBox.Max);

            double minX = Math.Min(localMin.X, localMax.X);
            double maxX = Math.Max(localMin.X, localMax.X);
            double minY = Math.Min(localMin.Y, localMax.Y);
            double maxY = Math.Max(localMin.Y, localMax.Y);
            double minZ = Math.Min(localMin.Z, localMax.Z);
            double maxZ = Math.Max(localMin.Z, localMax.Z);

            double halfWidth = (maxX - minX) / 2.0;
            double halfHeight = (maxY - minY) / 2.0;
            double halfDepth = (maxZ - minZ) / 2.0;
            double offset = GetSectionBoxOffset();

            BoundingBoxXYZ cropBox = new BoundingBoxXYZ();
            cropBox.Transform = newTransform;
            cropBox.Min = new XYZ(-halfWidth - offset, -halfHeight - offset, -halfDepth - offset);
            cropBox.Max = new XYZ(halfWidth + offset, halfHeight + offset, halfDepth + offset);

            return cropBox;
        }

        public void ApplyViewTemplate(View view, int? templateId)
        {
            if (!templateId.HasValue)
            {
                return;
            }

#pragma warning disable CS0618
            ElementId id = new ElementId(templateId.Value);
#pragma warning restore CS0618

            if (view.IsValidViewTemplate(id))
            {
                view.ViewTemplateId = id;
                Logger.Debug($"Applied view template Id {templateId.Value} to view '{view.Name}'.");
            }
            else
            {
                Logger.Warn($"View template Id {templateId.Value} is not valid for view '{view.Name}' (ViewType {view.ViewType}).");
            }
        }

        private ElementId ResolveViewFamilyTypeId(Document doc, ViewFamily viewFamily, int? viewFamilyTypeId)
        {
            if (viewFamilyTypeId.HasValue)
            {
#pragma warning disable CS0618
                ElementId selectedId = new ElementId(viewFamilyTypeId.Value);
#pragma warning restore CS0618
                ViewFamilyType selectedType = doc.GetElement(selectedId) as ViewFamilyType;
                if (selectedType != null && selectedType.ViewFamily == viewFamily)
                {
                    return selectedId;
                }

                Logger.Warn($"Selected view family type {viewFamilyTypeId.Value} is not valid for {viewFamily}. Using default.");
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(vft => vft.ViewFamily == viewFamily)
                .Select(vft => vft.Id)
                .FirstOrDefault();
        }

        private ElementId ResolveViewFamilyTypeId(Document doc, ViewFamily viewFamily, ElementId selectedTypeId)
        {
            if (selectedTypeId != null)
            {
                ViewFamilyType selectedType = doc.GetElement(selectedTypeId) as ViewFamilyType;
                if (selectedType != null && selectedType.ViewFamily == viewFamily)
                {
                    return selectedTypeId;
                }
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(vft => vft.ViewFamily == viewFamily)
                .Select(vft => vft.Id)
                .FirstOrDefault();
        }
    }
}
