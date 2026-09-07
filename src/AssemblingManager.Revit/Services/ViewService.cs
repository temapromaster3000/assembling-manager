using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class ViewService
    {
        public const string PlanSuffix = "";
        public const string FrontViewSuffix = "_Вид спереди";
        public const string BackViewSuffix = "_Вид сзади";
        public const string RightViewSuffix = "_Вид справа";
        public const string LeftViewSuffix = "_Вид слева";
        public const string View3DSuffix = "";

        private readonly IReadOnlyList<ViewTypeInfo> _viewTypes = new List<ViewTypeInfo>
        {
            new ViewTypeInfo(ViewType.Plan, PlanSuffix, "План", ViewKindPlan),
            new ViewTypeInfo(ViewType.FrontView, FrontViewSuffix, "Вид спереди", ViewKindFrontView),
            new ViewTypeInfo(ViewType.BackView, BackViewSuffix, "Вид сзади", ViewKindBackView),
            new ViewTypeInfo(ViewType.RightView, RightViewSuffix, "Вид справа", ViewKindRightView),
            new ViewTypeInfo(ViewType.LeftView, LeftViewSuffix, "Вид слева", ViewKindLeftView),
            new ViewTypeInfo(ViewType.View3D, View3DSuffix, "3D вид", ViewKind3D)
        };

        public const string ViewKindPlan = "Plan";
        public const string ViewKindFrontView = "FrontView";
        public const string ViewKindBackView = "BackView";
        public const string ViewKindRightView = "RightView";
        public const string ViewKindLeftView = "LeftView";
        public const string ViewKind3D = "View3D";
        public const string ViewKindSchedule = "Schedule";

        public void DeleteViewsByNames(Document doc, IEnumerable<string> viewNames, Type viewType = null)
        {
            HashSet<string> names = new HashSet<string>(viewNames);

            List<View> viewsToDelete = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => names.Contains(v.Name) && (viewType == null || viewType.IsInstanceOfType(v)))
                .ToList();

            if (viewsToDelete.Count > 0)
            {
                foreach (View view in viewsToDelete)
                {
                    UnlockView(view);
                    Logger.Info($"Deleting view '{view.Name}'.");
                }

                doc.Delete(viewsToDelete.Select(v => v.Id).ToList());
            }
        }

        public void LockView(View view)
        {
            View3D view3D = view as View3D;
            if (view3D == null || !view.IsValidObject)
            {
                return;
            }

            try
            {
                if (!view3D.HasBeenLocked())
                {
                    if (view3D.IsPerspective)
                    {
                        view3D.RestoreOrientationAndLock();
                    }
                    else
                    {
                        view3D.SaveOrientationAndLock();
                    }

                    Logger.Debug($"View3D '{view3D.Name}' locked.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not lock view3D '{view3D.Name}': {ex.Message}");
            }
        }

        public void UnlockView(View view)
        {
            View3D view3D = view as View3D;
            if (view3D == null || !view.IsValidObject)
            {
                return;
            }

            try
            {
                if (view3D.HasBeenLocked())
                {
                    view3D.Unlock();
                    Logger.Debug($"View3D '{view3D.Name}' unlocked.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not unlock view3D '{view3D.Name}': {ex.Message}");
            }
        }

        public List<ViewConflictItem> FindExistingViewConflicts(Document doc, IEnumerable<AssemblyInstance> assemblies, ViewCreationOptions options)
        {
            List<ViewConflictItem> conflicts = new List<ViewConflictItem>();

            foreach (AssemblyInstance assembly in assemblies)
            {
                foreach (ViewTypeInfo viewType in _viewTypes)
                {
                    if (!IsViewTypeSelected(options, viewType.Type))
                    {
                        continue;
                    }

                    string viewName = assembly.Name + viewType.Suffix;
                    if (ViewExists(doc, viewName, viewType.Type))
                    {
                        conflicts.Add(new ViewConflictItem
                        {
                            AssemblyName = assembly.Name,
                            ViewName = viewName,
                            ViewTypeDisplayName = viewType.DisplayName,
                            ViewKind = viewType.Kind,
                            Action = ConflictAction.Keep
                        });
                    }
                }
            }

            return conflicts;
        }

        public List<PlannedViewItem> FindMissingViews(Document doc, IEnumerable<AssemblyInstance> assemblies, ViewCreationOptions options)
        {
            List<PlannedViewItem> missing = new List<PlannedViewItem>();

            foreach (AssemblyInstance assembly in assemblies)
            {
                foreach (ViewTypeInfo viewType in _viewTypes)
                {
                    if (!IsViewTypeSelected(options, viewType.Type))
                    {
                        continue;
                    }

                    string viewName = assembly.Name + viewType.Suffix;
                    if (!ViewExists(doc, viewName, viewType.Type))
                    {
                        missing.Add(new PlannedViewItem
                        {
                            AssemblyName = assembly.Name,
                            ViewName = viewName,
                            ViewTypeDisplayName = viewType.DisplayName,
                            ViewKind = viewType.Kind,
                            Create = false
                        });
                    }
                }
            }

            return missing;
        }

        private bool ViewExists(Document doc, string viewName, ViewType viewType)
        {
            Type expectedType;

            switch (viewType)
            {
                case ViewType.Plan:
                    expectedType = typeof(ViewPlan);
                    break;
                case ViewType.FrontView:
                case ViewType.BackView:
                case ViewType.RightView:
                case ViewType.LeftView:
                    expectedType = typeof(ViewSection);
                    break;
                case ViewType.View3D:
                    expectedType = typeof(View3D);
                    break;
                default:
                    expectedType = typeof(View);
                    break;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name == viewName && expectedType.IsInstanceOfType(v));
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
            public string Kind { get; }

            public ViewTypeInfo(ViewType type, string suffix, string displayName, string kind)
            {
                Type = type;
                Suffix = suffix;
                DisplayName = displayName;
                Kind = kind;
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

        public View GetViewByName(Document doc, string viewName, Type viewType = null)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.Name == viewName && (viewType == null || viewType.IsInstanceOfType(v)));
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

            viewPlan.CropBox = BuildPlanCropBox(bbox);

            Level level = doc.GetElement(levelId) as Level;
            double levelElevationMm = (level?.Elevation ?? 0.0) / MillimetersToFeet;

            double viewRangeOffsetMm = 5000;
            double cutPlaneElevationMm = (GetRoundedPlanMinZMm(bbox) + GetRoundedPlanMaxZMm(bbox)) / 2.0;
            PlanViewRange planViewRange = viewPlan.GetViewRange();

            planViewRange.SetLevelId(PlanViewPlane.TopClipPlane, levelId);
            planViewRange.SetLevelId(PlanViewPlane.CutPlane, levelId);
            planViewRange.SetLevelId(PlanViewPlane.BottomClipPlane, levelId);
            planViewRange.SetLevelId(PlanViewPlane.ViewDepthPlane, levelId);

            planViewRange.SetOffset(PlanViewPlane.TopClipPlane, (GetRoundedPlanMaxZMm(bbox) + viewRangeOffsetMm - levelElevationMm) * MillimetersToFeet);
            planViewRange.SetOffset(PlanViewPlane.CutPlane, (cutPlaneElevationMm - levelElevationMm) * MillimetersToFeet);
            planViewRange.SetOffset(PlanViewPlane.BottomClipPlane, (GetRoundedPlanMinZMm(bbox) - viewRangeOffsetMm - levelElevationMm) * MillimetersToFeet);
            planViewRange.SetOffset(PlanViewPlane.ViewDepthPlane, (GetRoundedPlanMinZMm(bbox) - viewRangeOffsetMm - levelElevationMm) * MillimetersToFeet);

            viewPlan.SetViewRange(planViewRange);
        }

        private static double GetRoundedPlanMinZMm(BoundingBoxXYZ bbox)
        {
            return RoundToHundred(bbox.Min.Z / MillimetersToFeet, false);
        }

        private static double GetRoundedPlanMaxZMm(BoundingBoxXYZ bbox)
        {
            return RoundToHundred(bbox.Max.Z / MillimetersToFeet, true);
        }

        private static BoundingBoxXYZ BuildPlanCropBox(BoundingBoxXYZ bbox)
        {
            const double cropOffsetMm = 500;

            BoundingBoxXYZ cropBox = new BoundingBoxXYZ();
            cropBox.Min = new XYZ(
                bbox.Min.X - cropOffsetMm * MillimetersToFeet,
                bbox.Min.Y - cropOffsetMm * MillimetersToFeet,
                (GetRoundedPlanMinZMm(bbox) - cropOffsetMm) * MillimetersToFeet);
            cropBox.Max = new XYZ(
                bbox.Max.X + cropOffsetMm * MillimetersToFeet,
                bbox.Max.Y + cropOffsetMm * MillimetersToFeet,
                (GetRoundedPlanMaxZMm(bbox) + cropOffsetMm) * MillimetersToFeet);
            return cropBox;
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

        public void UpdatePlanViewGeometry(ViewPlan viewPlan, string assemblyName, BoundingBoxXYZ bbox)
        {
            if (viewPlan == null || !viewPlan.IsValidObject)
            {
                return;
            }

            string viewName = assemblyName + PlanSuffix;

            try
            {
                BoundingBoxXYZ currentCrop = viewPlan.CropBox;
                BoundingBoxXYZ newCrop = BuildPlanCropBox(bbox);
                viewPlan.CropBox = UnionBoundingBox(currentCrop, newCrop);

                ExpandPlanViewRange(viewPlan, bbox);

                Logger.Info($"Updated plan view '{viewName}': crop box and view range expanded.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not update plan view '{viewName}': {ex}");
            }
        }

        public void UpdateSectionViewGeometry(ViewSection viewSection, string assemblyName, string suffix, BoundingBoxXYZ bbox)
        {
            if (viewSection == null || !viewSection.IsValidObject)
            {
                return;
            }

            string viewName = assemblyName + suffix;

            try
            {
                BoundingBoxXYZ currentCrop = viewSection.CropBox;
                Transform transform = currentCrop.Transform;
                Transform inverse = transform.Inverse;

                XYZ bboxMinLocal = inverse.OfPoint(bbox.Min);
                XYZ bboxMaxLocal = inverse.OfPoint(bbox.Max);
                double offset = GetSectionBoxOffset();

                BoundingBoxXYZ newCrop = new BoundingBoxXYZ();
                newCrop.Transform = transform;
                newCrop.Min = new XYZ(
                    Math.Min(currentCrop.Min.X, Math.Min(bboxMinLocal.X, bboxMaxLocal.X) - offset),
                    Math.Min(currentCrop.Min.Y, Math.Min(bboxMinLocal.Y, bboxMaxLocal.Y) - offset),
                    Math.Min(currentCrop.Min.Z, Math.Min(bboxMinLocal.Z, bboxMaxLocal.Z) - offset));
                newCrop.Max = new XYZ(
                    Math.Max(currentCrop.Max.X, Math.Max(bboxMinLocal.X, bboxMaxLocal.X) + offset),
                    Math.Max(currentCrop.Max.Y, Math.Max(bboxMinLocal.Y, bboxMaxLocal.Y) + offset),
                    Math.Max(currentCrop.Max.Z, Math.Max(bboxMinLocal.Z, bboxMaxLocal.Z) + offset));

                viewSection.CropBox = newCrop;
                Logger.Info($"Updated section view '{viewName}': crop box expanded.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not update section view '{viewName}': {ex}");
            }
        }

        public void Update3DViewGeometry(View3D view3D, string assemblyName, BoundingBoxXYZ bbox)
        {
            if (view3D == null || !view3D.IsValidObject)
            {
                return;
            }

            string viewName = assemblyName + View3DSuffix;

            try
            {
                UnlockView(view3D);

                BoundingBoxXYZ currentSectionBox = view3D.GetSectionBox();
                BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                sectionBox.Min = new XYZ(
                    Math.Min(currentSectionBox.Min.X, bbox.Min.X),
                    Math.Min(currentSectionBox.Min.Y, bbox.Min.Y),
                    Math.Min(currentSectionBox.Min.Z, bbox.Min.Z));
                sectionBox.Max = new XYZ(
                    Math.Max(currentSectionBox.Max.X, bbox.Max.X),
                    Math.Max(currentSectionBox.Max.Y, bbox.Max.Y),
                    Math.Max(currentSectionBox.Max.Z, bbox.Max.Z));

                view3D.SetSectionBox(sectionBox);

                LockView(view3D);
                Logger.Info($"Updated 3D view '{viewName}': section box expanded.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not update 3D view '{viewName}': {ex}");
            }
        }

        private static BoundingBoxXYZ UnionBoundingBox(BoundingBoxXYZ current, BoundingBoxXYZ additional)
        {
            Transform transform = current.Transform;
            Transform inverse = transform.Inverse;

            XYZ additionalMinLocal = inverse.OfPoint(additional.Min);
            XYZ additionalMaxLocal = inverse.OfPoint(additional.Max);

            BoundingBoxXYZ union = new BoundingBoxXYZ();
            union.Transform = transform;
            union.Min = new XYZ(
                Math.Min(current.Min.X, Math.Min(additionalMinLocal.X, additionalMaxLocal.X)),
                Math.Min(current.Min.Y, Math.Min(additionalMinLocal.Y, additionalMaxLocal.Y)),
                Math.Min(current.Min.Z, Math.Min(additionalMinLocal.Z, additionalMaxLocal.Z)));
            union.Max = new XYZ(
                Math.Max(current.Max.X, Math.Max(additionalMinLocal.X, additionalMaxLocal.X)),
                Math.Max(current.Max.Y, Math.Max(additionalMinLocal.Y, additionalMaxLocal.Y)),
                Math.Max(current.Max.Z, Math.Max(additionalMinLocal.Z, additionalMaxLocal.Z)));
            return union;
        }

        private void ExpandPlanViewRange(ViewPlan viewPlan, BoundingBoxXYZ bbox)
        {
            const double viewRangeOffsetMm = 5000;
            Document doc = viewPlan.Document;

            double newTopAbsMm = GetRoundedPlanMaxZMm(bbox) + viewRangeOffsetMm;
            double newBottomAbsMm = GetRoundedPlanMinZMm(bbox) - viewRangeOffsetMm;
            double newCutAbsMm = (GetRoundedPlanMinZMm(bbox) + GetRoundedPlanMaxZMm(bbox)) / 2.0;

            PlanViewRange range = viewPlan.GetViewRange();

            double topLevelElevationMm = GetPlaneLevelElevationMm(doc, range, PlanViewPlane.TopClipPlane);
            double cutLevelElevationMm = GetPlaneLevelElevationMm(doc, range, PlanViewPlane.CutPlane);
            double bottomLevelElevationMm = GetPlaneLevelElevationMm(doc, range, PlanViewPlane.BottomClipPlane);
            double depthLevelElevationMm = GetPlaneLevelElevationMm(doc, range, PlanViewPlane.ViewDepthPlane);

            double topAbsMm = topLevelElevationMm + range.GetOffset(PlanViewPlane.TopClipPlane) / MillimetersToFeet;
            double cutAbsMm = cutLevelElevationMm + range.GetOffset(PlanViewPlane.CutPlane) / MillimetersToFeet;
            double bottomAbsMm = bottomLevelElevationMm + range.GetOffset(PlanViewPlane.BottomClipPlane) / MillimetersToFeet;
            double depthAbsMm = depthLevelElevationMm + range.GetOffset(PlanViewPlane.ViewDepthPlane) / MillimetersToFeet;

            double newTopAbs = Math.Max(topAbsMm, newTopAbsMm);
            double newBottomAbs = Math.Min(bottomAbsMm, newBottomAbsMm);
            double newDepthAbs = Math.Min(depthAbsMm, newBottomAbsMm);
            double newCut = (cutAbsMm > newTopAbs || cutAbsMm < newBottomAbs) ? newCutAbsMm : cutAbsMm;

            range.SetOffset(PlanViewPlane.TopClipPlane, (newTopAbs - topLevelElevationMm) * MillimetersToFeet);
            range.SetOffset(PlanViewPlane.CutPlane, (newCut - cutLevelElevationMm) * MillimetersToFeet);
            range.SetOffset(PlanViewPlane.BottomClipPlane, (newBottomAbs - bottomLevelElevationMm) * MillimetersToFeet);
            range.SetOffset(PlanViewPlane.ViewDepthPlane, (newDepthAbs - depthLevelElevationMm) * MillimetersToFeet);

            viewPlan.SetViewRange(range);
        }

        private static double GetPlaneLevelElevationMm(Document doc, PlanViewRange range, PlanViewPlane plane)
        {
            Level level = doc.GetElement(range.GetLevelId(plane)) as Level;
            return (level?.Elevation ?? 0.0) / MillimetersToFeet;
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
                ViewTemplateService viewTemplateService = new ViewTemplateService();
                View template = view.Document.GetElement(id) as View;
                bool controlsFilters = viewTemplateService.IsTemplateLockingFilters(view.Document, templateId.Value);
                string templateName = template != null ? template.Name : $"Id {templateId.Value}";
                Logger.Debug($"Applied view template '{templateName}' (Id {templateId.Value}) to view '{view.Name}' (controls filters: {controlsFilters}).");
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
