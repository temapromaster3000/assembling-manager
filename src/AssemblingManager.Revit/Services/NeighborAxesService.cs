using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class NeighborAxesTask
    {
        public string AssemblyName { get; set; }
        public View View { get; set; }
        public string ViewName { get; set; }
        public string AxisName { get; set; }
        public string EndLabel { get; set; }
        public XYZ Point { get; set; }
        public bool AxisIsVertical { get; set; }
        public double LineCoord { get; set; }
        public double Tolerance { get; set; }
        public bool ShowTop { get; set; }
        public bool ShowBottom { get; set; }
        public bool ShowLeft { get; set; }
        public bool ShowRight { get; set; }
        public string TopName { get; set; }
        public string BottomName { get; set; }
        public string LeftName { get; set; }
        public string RightName { get; set; }
        public string Description { get; set; }
    }

    public class NeighborAxesService
    {
        private const double MillimetersToFeet = 1.0 / 304.8;
        private const double StraightnessTolerance = 0.99;
        private const double OrientationMaxDeviationDegrees = 20.0;
        private const double MatchToleranceFactor = 3.0;

        public const string MarkerFamilyName = "Ближайшие оси - марка";

        private const string VisibilityTopParameter = "Верхняя ось";
        private const string VisibilityBottomParameter = "Нижняя ось";
        private const string VisibilityLeftParameter = "Левая ось";
        private const string VisibilityRightParameter = "Правая ось";

        private const string LabelTopParameter = "Имя оси сверху";
        private const string LabelBottomParameter = "Имя оси снизу";
        private const string LabelLeftParameter = "Имя оси слева";
        private const string LabelRightParameter = "Имя оси справа";

        private readonly HashSet<string> _reportedMissingParameters =
            new HashSet<string>(StringComparer.Ordinal);

        private sealed class AxisScreenInfo
        {
            public Grid Grid { get; set; }
            public string Name { get; set; }
            public Line Line { get; set; }
            public XYZ View1 { get; set; }
            public XYZ View2 { get; set; }
            public bool IsVertical { get; set; }
            public double PerpCoord { get; set; }
            public bool End0Dragged { get; set; }
            public bool End1Dragged { get; set; }
        }

        private sealed class ViewEntry
        {
            public View View { get; set; }
        }

        private sealed class CropBounds
        {
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
        }

        private sealed class CreatedMarker
        {
            public FamilyInstance Instance { get; set; }
            public NeighborAxesTask Task { get; set; }
        }

        public FamilySymbol FindMarkerSymbol(Document doc)
        {
            Family family = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name == MarkerFamilyName);

            if (family == null)
            {
                return null;
            }

            return family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(s => s != null)
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public List<string> GetWorksetNames(Document doc)
        {
            List<string> names = new List<string>();

            if (doc == null || !doc.IsWorkshared)
            {
                return names;
            }

            try
            {
                foreach (Workset workset in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets())
                {
                    names.Add(workset.Name);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read worksets: {ex.Message}");
            }

            names.Sort(new NaturalStringComparer());
            return names;
        }

        public WorksetId ResolveGridWorkset(Document doc, string worksetName)
        {
            if (doc == null || !doc.IsWorkshared || string.IsNullOrWhiteSpace(worksetName))
            {
                return null;
            }

            try
            {
                foreach (Workset workset in new FilteredWorksetCollector(doc).Cast<Workset>())
                {
                    if (workset.Name == worksetName)
                    {
                        return workset.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not resolve workset '{worksetName}': {ex.Message}");
            }

            return null;
        }

        public List<NeighborAxesTask> BuildTasks(
            Document doc,
            IEnumerable<AssemblyInstance> assemblies,
            NeighborAxesSettings settings,
            WorksetId gridWorksetId,
            NeighborAxesResult result)
        {
            List<NeighborAxesTask> tasks = new List<NeighborAxesTask>();
            ViewService viewService = new ViewService();

            string[] sectionSuffixes =
            {
                ViewService.FrontViewSuffix,
                ViewService.BackViewSuffix,
                ViewService.RightViewSuffix,
                ViewService.LeftViewSuffix
            };

            foreach (AssemblyInstance assembly in assemblies)
            {
                if (assembly == null || !assembly.IsValidObject)
                {
                    continue;
                }

                List<ViewEntry> views = new List<ViewEntry>();

                ViewPlan plan = viewService.GetViewByName(doc, assembly.Name, typeof(ViewPlan)) as ViewPlan;
                if (plan != null)
                {
                    views.Add(new ViewEntry { View = plan });
                }
                else
                {
                    Logger.Warn($"Assembly '{assembly.Name}': plan view not found. Skipped.");
                }

                foreach (string suffix in sectionSuffixes)
                {
                    ViewSection section = viewService.GetViewByName(doc, assembly.Name + suffix, typeof(ViewSection)) as ViewSection;
                    if (section != null)
                    {
                        views.Add(new ViewEntry { View = section });
                    }
                    else
                    {
                        Logger.Warn($"Assembly '{assembly.Name}': section view '{assembly.Name + suffix}' not found. Skipped.");
                    }
                }

                foreach (ViewEntry entry in views)
                {
                    AnalyzeView(doc, entry.View, assembly, settings, gridWorksetId, result, tasks);
                }
            }

            return tasks;
        }

        public void ApplyTasks(Document doc, FamilySymbol symbol, List<NeighborAxesTask> tasks, NeighborAxesResult result)
        {
            if (tasks == null || tasks.Count == 0)
            {
                return;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            result.ViewsProcessedCount = tasks.Select(t => t.View.Id).Distinct().Count();

            List<FamilyInstance> toDelete = CollectMarkersToDelete(doc, tasks);

            foreach (FamilyInstance instance in toDelete)
            {
                doc.Delete(instance.Id);
                result.MarkersReplacedCount++;
            }

            Logger.Info($"Existing markers deleted: {result.MarkersReplacedCount}.");

            List<CreatedMarker> created = new List<CreatedMarker>();

            foreach (NeighborAxesTask task in tasks)
            {
                FamilyInstance marker = doc.Create.NewFamilyInstance(task.Point, symbol, task.View);
                SetMarkerParameters(marker, task, result);
                result.MarkersCreatedCount++;
                created.Add(new CreatedMarker { Instance = marker, Task = task });
                Logger.Info($"Marker created: {task.Description}");
            }

            doc.Regenerate();

            foreach (CreatedMarker item in created)
            {
                LocationPoint location = item.Instance.Location as LocationPoint;
                if (location == null)
                {
                    continue;
                }

                try
                {
                    XYZ viewPoint = item.Task.View.CropBox.Transform.Inverse.OfPoint(location.Point);
                    Logger.Info(
                        $"Marker readback: view '{item.Task.ViewName}', axis '{item.Task.AxisName}' ({item.Task.EndLabel}) " +
                        $"at view ({viewPoint.X:F3}, {viewPoint.Y:F3}).");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Marker readback failed: {ex.Message}");
                }
            }
        }

        private void AnalyzeView(
            Document doc,
            View view,
            AssemblyInstance assembly,
            NeighborAxesSettings settings,
            WorksetId gridWorksetId,
            NeighborAxesResult result,
            List<NeighborAxesTask> tasks)
        {
            string viewName = view.Name;

            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null)
            {
                Logger.Warn($"View '{viewName}': no crop box. Skipped.");
                result.ViewsSkippedCount++;
                return;
            }

            Transform transform = crop.Transform;
            Transform inverse = transform.Inverse;
            bool isPlan = view is ViewPlan;
            bool cropActive = view.CropBoxActive;
            XYZ viewDirection = transform.BasisZ;

            Logger.Debug($"[{viewName}] crop active: {cropActive}");

            double minX = Math.Min(crop.Min.X, crop.Max.X);
            double maxX = Math.Max(crop.Min.X, crop.Max.X);
            double minY = Math.Min(crop.Min.Y, crop.Max.Y);
            double maxY = Math.Max(crop.Min.Y, crop.Max.Y);

            CropBounds annotationCrop = GetAnnotationCropBounds(view, minX, maxX, minY, maxY);

            Logger.Debug(
                $"[{viewName}] crop ({minX:F3}, {minY:F3}) - ({maxX:F3}, {maxY:F3}); " +
                $"annotation crop ({annotationCrop.MinX:F3}, {annotationCrop.MinY:F3}) - " +
                $"({annotationCrop.MaxX:F3}, {annotationCrop.MaxY:F3})");

            List<AxisScreenInfo> visibleHorizontal = new List<AxisScreenInfo>();
            List<AxisScreenInfo> visibleVertical = new List<AxisScreenInfo>();

            foreach (Grid grid in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .Where(g => gridWorksetId == null || g.WorksetId == gridWorksetId))
            {
                AxisScreenInfo axis = ClassifyAxis(grid, view, inverse, viewDirection, isPlan, result.Warnings, viewName);
                if (axis == null)
                {
                    continue;
                }

                if (cropActive && !IsVisibleInCrop(axis, isPlan, minX, maxX, minY, maxY))
                {
                    continue;
                }

                if (axis.IsVertical)
                {
                    visibleVertical.Add(axis);
                }
                else
                {
                    visibleHorizontal.Add(axis);
                }
            }

            Logger.Info(
                $"View '{viewName}': visible horizontal axes {visibleHorizontal.Count}, " +
                $"vertical axes {visibleVertical.Count}.");

            List<AxisScreenInfo> modelHorizontal = new List<AxisScreenInfo>();
            List<AxisScreenInfo> modelVertical = new List<AxisScreenInfo>();

            if (visibleHorizontal.Count == 1 || visibleVertical.Count == 1)
            {
                foreach (Grid grid in new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .Where(g => gridWorksetId == null || g.WorksetId == gridWorksetId))
                {
                    AxisScreenInfo axis = ClassifyAxis(grid, view, inverse, viewDirection, isPlan, null, null);
                    if (axis == null)
                    {
                        continue;
                    }

                    if (axis.IsVertical)
                    {
                        modelVertical.Add(axis);
                    }
                    else
                    {
                        modelHorizontal.Add(axis);
                    }
                }
            }

            double scale = view.Scale > 0 ? view.Scale : 1.0;
            double radius = settings.RadiusMm * scale * MillimetersToFeet;

            int tasksBefore = tasks.Count;

            if (visibleHorizontal.Count > 1)
            {
                Logger.Info(
                    $"View '{viewName}': {visibleHorizontal.Count} horizontal (lettered) axes visible — processing not required.");
            }
            else if (visibleHorizontal.Count == 1)
            {
                BuildAxisTasks(view, assembly, viewName, visibleHorizontal[0], modelHorizontal, false, transform, radius, annotationCrop, cropActive, result, tasks);
            }

            if (visibleVertical.Count > 1)
            {
                Logger.Info(
                    $"View '{viewName}': {visibleVertical.Count} vertical (numbered) axes visible — processing not required.");
            }
            else if (visibleVertical.Count == 1)
            {
                BuildAxisTasks(view, assembly, viewName, visibleVertical[0], modelVertical, true, transform, radius, annotationCrop, cropActive, result, tasks);
            }

            if (tasks.Count == tasksBefore)
            {
                result.ViewsSkippedCount++;
            }
        }

        private AxisScreenInfo ClassifyAxis(
            Grid grid,
            View view,
            Transform inverse,
            XYZ viewDirection,
            bool isPlan,
            List<string> warnings,
            string viewName)
        {
            if (grid == null || !grid.IsValidObject)
            {
                return null;
            }

            Line modelLine = grid.Curve as Line;
            if (modelLine == null)
            {
                if (warnings != null)
                {
                    warnings.Add($"Вид «{viewName}»: ось «{grid.Name}» не прямая — пропущена.");
                }

                Logger.Warn($"Grid '{grid.Name}': curve is not a straight line. Skipped.");
                return null;
            }

            Line drawnLine = GetViewCurve(grid, view, modelLine, out bool end0Dragged, out bool end1Dragged);

            if (!isPlan)
            {
                XYZ direction = (modelLine.GetEndPoint(1) - modelLine.GetEndPoint(0)).Normalize();
                double dot = Math.Abs(direction.DotProduct(viewDirection));
                Logger.Debug($"[{viewName}] axis '{grid.Name}': |dot(view direction)| = {dot:F3}");

                if (dot < StraightnessTolerance)
                {
                    return null;
                }

                XYZ d1 = inverse.OfPoint(drawnLine.GetEndPoint(0));
                XYZ d2 = inverse.OfPoint(drawnLine.GetEndPoint(1));

                Logger.Debug(
                    $"[{viewName}] axis '{grid.Name}': view ends ({d1.X:F3}, {d1.Y:F3}) - ({d2.X:F3}, {d2.Y:F3})");

                return new AxisScreenInfo
                {
                    Grid = grid,
                    Name = grid.Name,
                    Line = drawnLine,
                    View1 = d1,
                    View2 = d2,
                    IsVertical = true,
                    PerpCoord = (d1.X + d2.X) / 2.0,
                    End0Dragged = end0Dragged,
                    End1Dragged = end1Dragged
                };
            }

            XYZ v1 = inverse.OfPoint(drawnLine.GetEndPoint(0));
            XYZ v2 = inverse.OfPoint(drawnLine.GetEndPoint(1));

            if (!TryClassifyOrientation(v1, v2, out bool isVertical))
            {
                if (warnings != null)
                {
                    warnings.Add($"Вид «{viewName}»: ось «{grid.Name}» косая — пропущена.");
                }

                Logger.Warn($"Grid '{grid.Name}' is skewed relative to the view. Skipped.");
                return null;
            }

            Logger.Debug(
                $"[{viewName}] axis '{grid.Name}': view ends ({v1.X:F3}, {v1.Y:F3}) - ({v2.X:F3}, {v2.Y:F3}), " +
                (isVertical ? "vertical" : "horizontal"));

            return new AxisScreenInfo
            {
                Grid = grid,
                Name = grid.Name,
                Line = drawnLine,
                View1 = v1,
                View2 = v2,
                IsVertical = isVertical,
                PerpCoord = isVertical ? (v1.X + v2.X) / 2.0 : (v1.Y + v2.Y) / 2.0,
                End0Dragged = end0Dragged,
                End1Dragged = end1Dragged
            };
        }

        private Line GetViewCurve(Grid grid, View view, Line modelLine, out bool end0Dragged, out bool end1Dragged)
        {
            end0Dragged = false;
            end1Dragged = false;

            bool typesKnown = false;
            DatumExtentType end0Type = DatumExtentType.Model;
            DatumExtentType end1Type = DatumExtentType.Model;

            try
            {
                end0Type = grid.GetDatumExtentTypeInView(DatumEnds.End0, view);
                end1Type = grid.GetDatumExtentTypeInView(DatumEnds.End1, view);
                typesKnown = true;
                end0Dragged = end0Type == DatumExtentType.ViewSpecific;
                end1Dragged = end1Type == DatumExtentType.ViewSpecific;
                Logger.Debug(
                    $"Grid '{grid.Name}': extent types End0 = {end0Type}, End1 = {end1Type}.");
            }
            catch (Exception ex)
            {
                Logger.Debug($"Grid '{grid.Name}': GetDatumExtentTypeInView failed: {ex.Message}");
            }

            if (typesKnown && (end0Dragged || end1Dragged))
            {
                Line viewSpecificLine = TryGetLine(grid, DatumExtentType.ViewSpecific, view);
                if (viewSpecificLine != null)
                {
                    return viewSpecificLine;
                }
            }

            Line modelCurveLine = TryGetLine(grid, DatumExtentType.Model, view);
            if (modelCurveLine != null)
            {
                return modelCurveLine;
            }

            return modelLine;
        }

        private static Line TryGetLine(Grid grid, DatumExtentType extentMode, View view)
        {
            try
            {
                IList<Curve> curves = grid.GetCurvesInView(extentMode, view);
                if (curves == null)
                {
                    return null;
                }

                foreach (Curve curve in curves)
                {
                    Line line = curve as Line;
                    if (line != null)
                    {
                        return line;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Grid '{grid.Name}': GetCurvesInView({extentMode}) failed: {ex.Message}");
            }

            return null;
        }

        private static bool IsBubbleEnabled(Grid grid, DatumEnds end, View view)
        {
            try
            {
                return grid.IsBubbleVisibleInView(end, view);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Grid '{grid.Name}': IsBubbleVisibleInView({end}) failed: {ex.Message}");
                return false;
            }
        }

        private static bool IsVisibleInCrop(
            AxisScreenInfo axis,
            bool isPlan,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (isPlan)
            {
                return SegmentIntersectsRect(axis.View1, axis.View2, minX, maxX, minY, maxY);
            }

            if (axis.PerpCoord < minX || axis.PerpCoord > maxX)
            {
                return false;
            }

            double low = Math.Min(axis.View1.Y, axis.View2.Y);
            double high = Math.Max(axis.View1.Y, axis.View2.Y);

            return high >= minY && low <= maxY;
        }

        private static CropBounds GetAnnotationCropBounds(View view, double minX, double maxX, double minY, double maxY)
        {
            CropBounds bounds = new CropBounds { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY };

            try
            {
                ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();

                if (manager.CanHaveAnnotationCrop)
                {
                    double scale = view.Scale > 0 ? view.Scale : 1.0;
                    bounds.MinX -= manager.LeftAnnotationCropOffset * scale;
                    bounds.MaxX += manager.RightAnnotationCropOffset * scale;
                    bounds.MinY -= manager.BottomAnnotationCropOffset * scale;
                    bounds.MaxY += manager.TopAnnotationCropOffset * scale;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"View '{view.Name}': annotation crop unavailable: {ex.Message}");
            }

            return bounds;
        }

        private void BuildAxisTasks(
            View view,
            AssemblyInstance assembly,
            string viewName,
            AxisScreenInfo axis,
            List<AxisScreenInfo> modelAxes,
            bool isVertical,
            Transform transform,
            double radius,
            CropBounds annotationCrop,
            bool cropActive,
            NeighborAxesResult result,
            List<NeighborAxesTask> tasks)
        {
            List<AxisScreenInfo> sorted = modelAxes
                .OrderBy(a => a.PerpCoord)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int index = sorted.FindIndex(a => a.Grid.Id == axis.Grid.Id);
            if (index < 0)
            {
                result.Warnings.Add($"Вид «{viewName}»: ось «{axis.Name}» не найдена среди осей модели.");
                Logger.Warn($"Axis '{axis.Name}' not found among model grids of view '{viewName}'.");
                return;
            }

            AxisScreenInfo previous = index > 0 ? sorted[index - 1] : null;
            AxisScreenInfo next = index + 1 < sorted.Count ? sorted[index + 1] : null;

            if (previous == null && next == null)
            {
                result.Warnings.Add($"Вид «{viewName}»: у оси «{axis.Name}» нет соседних осей — маркеры не поставлены.");
                Logger.Warn($"Axis '{axis.Name}' has no neighbor grids. Skipped.");
                return;
            }

            bool bubbleEnd0 = IsBubbleEnabled(axis.Grid, DatumEnds.End0, view);
            bool bubbleEnd1 = IsBubbleEnabled(axis.Grid, DatumEnds.End1, view);
            Logger.Debug($"[{viewName}] axis '{axis.Name}': bubbles End0 = {bubbleEnd0}, End1 = {bubbleEnd1}.");

            if (!bubbleEnd0 && !bubbleEnd1)
            {
                Logger.Debug($"[{viewName}] axis '{axis.Name}': no bubbles enabled. No markers.");
                return;
            }

            bool showLeft = false;
            bool showRight = false;
            bool showTop = false;
            bool showBottom = false;
            string leftName = null;
            string rightName = null;
            string topName = null;
            string bottomName = null;

            if (isVertical)
            {
                if (next != null)
                {
                    showRight = true;
                    rightName = next.Name;
                }

                if (previous != null)
                {
                    showLeft = true;
                    leftName = previous.Name;
                }
            }
            else
            {
                if (next != null)
                {
                    showTop = true;
                    topName = next.Name;
                }

                if (previous != null)
                {
                    showBottom = true;
                    bottomName = previous.Name;
                }
            }

            if (bubbleEnd0)
            {
                AddEndTask(view, assembly, viewName, axis, isVertical, axis.View1, transform, radius, annotationCrop,
                    cropActive, axis.End0Dragged,
                    showTop, showBottom, showLeft, showRight, topName, bottomName, leftName, rightName,
                    result, tasks);
            }

            if (bubbleEnd1)
            {
                AddEndTask(view, assembly, viewName, axis, isVertical, axis.View2, transform, radius, annotationCrop,
                    cropActive, axis.End1Dragged,
                    showTop, showBottom, showLeft, showRight, topName, bottomName, leftName, rightName,
                    result, tasks);
            }
        }

        private void AddEndTask(
            View view,
            AssemblyInstance assembly,
            string viewName,
            AxisScreenInfo axis,
            bool isVertical,
            XYZ endView,
            Transform transform,
            double radius,
            CropBounds annotationCrop,
            bool cropActive,
            bool endDragged,
            bool showTop,
            bool showBottom,
            bool showLeft,
            bool showRight,
            string topName,
            string bottomName,
            string leftName,
            string rightName,
            NeighborAxesResult result,
            List<NeighborAxesTask> tasks)
        {
            double midX = (axis.View1.X + axis.View2.X) / 2.0;
            double midY = (axis.View1.Y + axis.View2.Y) / 2.0;

            double outwardX = 0.0;
            double outwardY = 0.0;
            string endLabel;

            if (isVertical)
            {
                if (endView.Y <= midY)
                {
                    outwardY = -1.0;
                    endLabel = "низ";
                }
                else
                {
                    outwardY = 1.0;
                    endLabel = "верх";
                }
            }
            else
            {
                if (endView.X <= midX)
                {
                    outwardX = -1.0;
                    endLabel = "лево";
                }
                else
                {
                    outwardX = 1.0;
                    endLabel = "право";
                }
            }

            double endParallel = isVertical ? endView.Y : endView.X;
            double annotationMin = isVertical ? annotationCrop.MinY : annotationCrop.MinX;
            double annotationMax = isVertical ? annotationCrop.MaxY : annotationCrop.MaxX;

            bool clamp = cropActive && !endDragged;
            double clampedParallel = clamp
                ? Math.Min(Math.Max(endParallel, annotationMin), annotationMax)
                : endParallel;

            double pointX;
            double pointY;

            if (isVertical)
            {
                pointX = axis.PerpCoord;
                pointY = clampedParallel + outwardY * radius;
            }
            else
            {
                pointX = clampedParallel + outwardX * radius;
                pointY = axis.PerpCoord;
            }

            XYZ pointView = new XYZ(pointX, pointY, 0.0);
            XYZ worldPoint = transform.OfPoint(pointView);

            NeighborAxesTask task = new NeighborAxesTask
            {
                AssemblyName = assembly.Name,
                View = view,
                ViewName = viewName,
                AxisName = axis.Name,
                EndLabel = endLabel,
                AxisIsVertical = isVertical,
                LineCoord = axis.PerpCoord,
                Tolerance = MatchToleranceFactor * radius,
                Point = worldPoint,
                ShowTop = showTop,
                ShowBottom = showBottom,
                ShowLeft = showLeft,
                ShowRight = showRight,
                TopName = topName,
                BottomName = bottomName,
                LeftName = leftName,
                RightName = rightName
            };

            List<string> parts = new List<string>();
            if (showTop)
            {
                parts.Add($"сверху «{topName}»");
            }

            if (showBottom)
            {
                parts.Add($"снизу «{bottomName}»");
            }

            if (showLeft)
            {
                parts.Add($"слева «{leftName}»");
            }

            if (showRight)
            {
                parts.Add($"справа «{rightName}»");
            }

            task.Description = $"Сборка «{assembly.Name}», вид «{viewName}», ось «{axis.Name}» ({endLabel}): {string.Join(", ", parts)}";
            result.Details.Add(task.Description);

            string clampInfo = !cropActive
                ? "no clamp (crop off)"
                : endDragged
                    ? "no clamp (dragged end)"
                    : $"clamped to {clampedParallel:F3} (annotation crop {annotationMin:F3}..{annotationMax:F3})";

            Logger.Debug(
                $"[{viewName}] axis '{axis.Name}' ({endLabel}): end parallel {endParallel:F3}, {clampInfo}, " +
                $"point view ({pointView.X:F3}, {pointView.Y:F3}), " +
                $"world ({worldPoint.X:F3}, {worldPoint.Y:F3}, {worldPoint.Z:F3})");

            tasks.Add(task);
        }

        private List<FamilyInstance> CollectMarkersToDelete(Document doc, List<NeighborAxesTask> tasks)
        {
            List<FamilyInstance> toDelete = new List<FamilyInstance>();

            foreach (IGrouping<ElementId, NeighborAxesTask> group in tasks.GroupBy(t => t.View.Id))
            {
                List<NeighborAxesTask> viewTasks = group.ToList();

                foreach (FamilyInstance instance in new FilteredElementCollector(doc, group.Key)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(IsMarkerInstance))
                {
                    NeighborAxesTask owner = FindOwnerTask(instance, viewTasks);
                    if (owner != null)
                    {
                        toDelete.Add(instance);
                    }
                }
            }

            return toDelete;
        }

        private NeighborAxesTask FindOwnerTask(FamilyInstance instance, List<NeighborAxesTask> tasks)
        {
            LocationPoint location = instance.Location as LocationPoint;
            if (location == null)
            {
                return null;
            }

            XYZ worldPoint = location.Point;
            NeighborAxesTask best = null;
            double bestDistance = double.MaxValue;

            foreach (NeighborAxesTask task in tasks)
            {
                Transform inverse = task.View.CropBox.Transform.Inverse;
                XYZ viewPoint = inverse.OfPoint(worldPoint);

                double perp = task.AxisIsVertical ? viewPoint.X : viewPoint.Y;
                double distance = Math.Abs(perp - task.LineCoord);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = task;
                }
            }

            if (best == null || bestDistance > best.Tolerance)
            {
                return null;
            }

            return best;
        }

        private static bool IsMarkerInstance(FamilyInstance instance)
        {
            return instance != null
                && instance.IsValidObject
                && instance.Symbol != null
                && instance.Symbol.Family != null
                && instance.Symbol.Family.Name == MarkerFamilyName;
        }

        private void SetMarkerParameters(FamilyInstance marker, NeighborAxesTask task, NeighborAxesResult result)
        {
            SetYesNoParameter(marker, VisibilityTopParameter, task.ShowTop, result);
            SetYesNoParameter(marker, VisibilityBottomParameter, task.ShowBottom, result);
            SetYesNoParameter(marker, VisibilityLeftParameter, task.ShowLeft, result);
            SetYesNoParameter(marker, VisibilityRightParameter, task.ShowRight, result);

            SetTextParameter(marker, LabelTopParameter, task.ShowTop ? task.TopName : string.Empty, result);
            SetTextParameter(marker, LabelBottomParameter, task.ShowBottom ? task.BottomName : string.Empty, result);
            SetTextParameter(marker, LabelLeftParameter, task.ShowLeft ? task.LeftName : string.Empty, result);
            SetTextParameter(marker, LabelRightParameter, task.ShowRight ? task.RightName : string.Empty, result);
        }

        private void SetYesNoParameter(FamilyInstance marker, string parameterName, bool value, NeighborAxesResult result)
        {
            Parameter parameter = marker.LookupParameter(parameterName);
            if (parameter == null)
            {
                ReportMissingParameter(parameterName, result);
                return;
            }

            parameter.Set(value ? 1 : 0);
        }

        private void SetTextParameter(FamilyInstance marker, string parameterName, string value, NeighborAxesResult result)
        {
            Parameter parameter = marker.LookupParameter(parameterName);
            if (parameter == null)
            {
                ReportMissingParameter(parameterName, result);
                return;
            }

            parameter.Set(value ?? string.Empty);
        }

        private void ReportMissingParameter(string parameterName, NeighborAxesResult result)
        {
            if (_reportedMissingParameters.Add(parameterName))
            {
                result.Warnings.Add($"В семействе «{MarkerFamilyName}» не найден параметр «{parameterName}».");
                Logger.Warn($"Parameter '{parameterName}' not found in family '{MarkerFamilyName}'.");
            }
        }

        private static bool TryClassifyOrientation(XYZ v1, XYZ v2, out bool isVertical)
        {
            double dx = Math.Abs(v2.X - v1.X);
            double dy = Math.Abs(v2.Y - v1.Y);
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

            double deviationFromHorizontal = Math.Min(angle, 180.0 - angle);
            double deviationFromVertical = Math.Abs(angle - 90.0);

            if (deviationFromHorizontal <= OrientationMaxDeviationDegrees)
            {
                isVertical = false;
                return true;
            }

            if (deviationFromVertical <= OrientationMaxDeviationDegrees)
            {
                isVertical = true;
                return true;
            }

            isVertical = false;
            return false;
        }

        private static bool SegmentIntersectsRect(XYZ a, XYZ b, double minX, double maxX, double minY, double maxY)
        {
            double t0 = 0.0;
            double t1 = 1.0;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;

            double[] p = { -dx, dx, -dy, dy };
            double[] q = { a.X - minX, maxX - a.X, a.Y - minY, maxY - a.Y };

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-12)
                {
                    if (q[i] < 0)
                    {
                        return false;
                    }
                }
                else
                {
                    double r = q[i] / p[i];
                    if (p[i] < 0)
                    {
                        if (r > t1)
                        {
                            return false;
                        }

                        if (r > t0)
                        {
                            t0 = r;
                        }
                    }
                    else
                    {
                        if (r < t0)
                        {
                            return false;
                        }

                        if (r < t1)
                        {
                            t1 = r;
                        }
                    }
                }
            }

            return true;
        }
    }
}
