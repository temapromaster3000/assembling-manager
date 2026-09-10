using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class PipeAxisService
    {
        private const double MillimetersToFeet = 1.0 / 304.8;

        /// <summary>
        /// Оси короче этой величины в проекции на вид (мм) не рисуются:
        /// стояки на планах, трубы вдоль направления взгляда на разрезах.
        /// </summary>
        private const double MinProjectedLengthMm = 20.0;

        private const double OcclusionSampleStepMm = 25.0;
        private const int MinSamples = 5;
        private const int MaxSamples = 50;

        public sealed class AssemblyView
        {
            public AssemblyInstance Assembly { get; set; }

            public View View { get; set; }
        }

        private sealed class OcclusionStats
        {
            public int Rays { get; set; }

            public int Hits { get; set; }
        }

        private sealed class PipeAxisData
        {
            public Pipe Pipe { get; set; }

            public Line ModelLine { get; set; }

            public XYZ V0 { get; set; }

            public double Dx { get; set; }

            public double Dy { get; set; }

            public double ProjectedLengthMm { get; set; }

            public int SampleCount { get; set; }

            public List<bool> Mask { get; set; }
        }

        private sealed class ViewContext
        {
            public View View { get; set; }

            public Transform CropTransform { get; set; }

            public XYZ RayDirection { get; set; }

            public List<ElementId> TargetIds { get; set; }

            public List<ElementId> Phases { get; set; }

            public List<PipeAxisData> Pipes { get; } = new List<PipeAxisData>();

            public int SkippedCount { get; set; }

            public int DeletedCount { get; set; }

            public OcclusionStats Stats { get; } = new OcclusionStats();
        }

        public List<GraphicsStyle> GetLineStyles(Document doc)
        {
            List<GraphicsStyle> styles = new List<GraphicsStyle>();

            Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory == null)
            {
                return styles;
            }

            foreach (Category subcategory in linesCategory.SubCategories)
            {
                try
                {
                    GraphicsStyle style = subcategory.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (style != null)
                    {
                        styles.Add(style);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Line style '{subcategory.Name}': GetGraphicsStyle failed: {ex.Message}");
                }
            }

            styles = styles
                .OrderBy(s => s.GraphicsStyleCategory?.Name ?? string.Empty, new NaturalStringComparer())
                .ToList();

            Logger.Info($"Line styles found: {styles.Count}.");
            return styles;
        }

        public List<AssemblyView> CollectViews(Document doc, IEnumerable<AssemblyInstance> assemblies, PipeAxisResult result)
        {
            List<AssemblyView> views = new List<AssemblyView>();
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

                ViewPlan plan = viewService.GetViewByName(doc, assembly.Name, typeof(ViewPlan)) as ViewPlan;
                if (plan != null)
                {
                    views.Add(new AssemblyView { Assembly = assembly, View = plan });
                }
                else
                {
                    result.Warnings.Add($"Сборка «{assembly.Name}»: план не найден — пропущен.");
                    Logger.Warn($"Assembly '{assembly.Name}': plan view not found. Skipped.");
                }

                foreach (string suffix in sectionSuffixes)
                {
                    ViewSection section = viewService.GetViewByName(doc, assembly.Name + suffix, typeof(ViewSection)) as ViewSection;
                    if (section != null)
                    {
                        views.Add(new AssemblyView { Assembly = assembly, View = section });
                    }
                    else
                    {
                        Logger.Warn($"Assembly '{assembly.Name}': section view '{assembly.Name + suffix}' not found. Skipped.");
                    }
                }
            }

            Logger.Info($"Views collected: {views.Count}.");
            return views;
        }

        public void ApplyAxes(Document doc, GraphicsStyle lineStyle, List<AssemblyView> views, PipeAxisSettings settings, PipeAxisResult result)
        {
            if (views == null || views.Count == 0)
            {
                return;
            }

            bool occlusionEnabled = settings.SkipOccludedSegments;
            Dictionary<ElementId, List<ElementId>> insulations = occlusionEnabled
                ? CollectInsulationsByHost(doc)
                : null;

            List<ElementId> groupOrder = new List<ElementId>();
            Dictionary<ElementId, List<AssemblyView>> groups = new Dictionary<ElementId, List<AssemblyView>>();

            foreach (AssemblyView entry in views)
            {
                ElementId key = entry.Assembly != null ? entry.Assembly.Id : ElementId.InvalidElementId;
                if (!groups.TryGetValue(key, out List<AssemblyView> group))
                {
                    group = new List<AssemblyView>();
                    groups[key] = group;
                    groupOrder.Add(key);
                }

                group.Add(entry);
            }

            foreach (ElementId key in groupOrder)
            {
                List<AssemblyView> group = groups[key];
                AssemblyInstance assembly = group[0].Assembly;

                bool occlusionForAssembly = false;
                View3D assemblyRayView = null;

                if (occlusionEnabled && assembly != null)
                {
                    assemblyRayView = FindAssemblyRayView(doc, assembly);

                    if (assemblyRayView != null)
                    {
                        LogRayViewInfo(doc, assembly, assemblyRayView);
                        occlusionForAssembly = true;
                    }

                    if (!occlusionForAssembly)
                    {
                        result.Warnings.Add(
                            $"Для сборки «{assembly.Name}» не найден 3D-вид — проверка перекрытий пропущена, " +
                            "оси построены целиком. Создайте 3D-вид и повторите запуск.");
                    }
                }

                List<ViewContext> contexts = new List<ViewContext>();

                foreach (AssemblyView entry in group)
                {
                    ViewContext context = PrepareView(doc, entry, lineStyle, occlusionForAssembly ? assemblyRayView : null, result);
                    if (context != null)
                    {
                        contexts.Add(context);
                    }
                }

                if (!occlusionForAssembly)
                {
                    foreach (ViewContext context in contexts)
                    {
                        FinalizeView(doc, context, lineStyle, false, result);
                    }

                    continue;
                }

                ElementId originalPhase = GetViewPhaseId(assemblyRayView);
                bool phaseChanged = false;

                try
                {
                    foreach (ViewContext context in contexts)
                    {
                        int passCount = Math.Max(context.Phases.Count, 1);

                        for (int passIndex = 0; passIndex < context.Phases.Count; passIndex++)
                        {
                            ElementId phase = context.Phases[passIndex];
                            string phaseName = phase != null ? GetElementName(doc, phase) : "текущая";

                            if (phase != null && phase != GetViewPhaseId(assemblyRayView))
                            {
                                if (!SetViewPhase(doc, assemblyRayView, phase))
                                {
                                    result.Warnings.Add(
                                        $"Не удалось установить стадию «{phaseName}» для 3D-вида сборки «{assembly.Name}» — прогон пропущен.");
                                    continue;
                                }

                                doc.Regenerate();
                                phaseChanged = true;
                            }

                            Logger.Info($"Ray pass {passIndex + 1}/{passCount} for view '{context.View.Name}': phase '{phaseName}'.");

                            using (ReferenceIntersector intersector = new ReferenceIntersector(
                                context.TargetIds, FindReferenceTarget.Face, assemblyRayView))
                            {
                                OcclusionStats passStats = new OcclusionStats();
                                ComputeMasksForPass(context, intersector, insulations, passStats, result);

                                context.Stats.Rays += passStats.Rays;
                                context.Stats.Hits += passStats.Hits;
                                result.RaysCastCount += passStats.Rays;
                                result.HitsReceivedCount += passStats.Hits;

                                if (passStats.Rays > 0 && passStats.Hits == 0 && context.Pipes.Count > 0)
                                {
                                    string passInfo = $"прогон {passIndex + 1}/{passCount}, стадия «{phaseName}»";
                                    RunOcclusionProbe(doc, context.View, assemblyRayView, context.TargetIds, insulations, context.Pipes[0].Pipe, result, passInfo);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (phaseChanged && originalPhase != null)
                    {
                        SetViewPhase(doc, assemblyRayView, originalPhase);
                        doc.Regenerate();
                    }
                }

                foreach (ViewContext context in contexts)
                {
                    FinalizeView(doc, context, lineStyle, true, result);
                }
            }
        }

        public void DeleteAxes(Document doc, GraphicsStyle lineStyle, List<AssemblyView> views, PipeAxisResult result)
        {
            string styleName = GetStyleName(lineStyle);

            foreach (AssemblyView entry in views)
            {
                View view = entry?.View;

                if (view == null || !view.IsValidObject)
                {
                    continue;
                }

                int deletedCount = DeleteAxisLines(doc, view, lineStyle, result);

                if (deletedCount > 0)
                {
                    result.ViewsProcessedCount++;
                }

                result.Details.Add($"Вид «{view.Name}»: удалено линий {deletedCount}.");
                Logger.Info($"View '{view.Name}' (style '{styleName}'): deleted {deletedCount}.");
            }
        }

        private ViewContext PrepareView(Document doc, AssemblyView entry, GraphicsStyle lineStyle, View3D assemblyRayView, PipeAxisResult result)
        {
            View view = entry?.View;

            if (view == null || !view.IsValidObject)
            {
                return null;
            }

            ViewContext context = new ViewContext
            {
                View = view
            };

            context.DeletedCount = DeleteAxisLines(doc, view, lineStyle, result);

            context.TargetIds = CollectRayTargets(doc, view);
            context.Phases = CollectTargetPhases(doc, context.TargetIds);

            Logger.Info($"View '{view.Name}': ray targets {context.TargetIds.Count}, phases {context.Phases.Count}.");

            List<Pipe> pipes = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .ToList();

            result.PipesFoundCount += pipes.Count;

            Transform cropTransform = view.CropBox?.Transform;
            Transform inverse = cropTransform?.Inverse;

            if (cropTransform == null || inverse == null)
            {
                result.Warnings.Add($"Вид «{view.Name}»: нет CropBox — оси не построены.");
                Logger.Warn($"View '{view.Name}': no crop box. Axes skipped.");
                return null;
            }

            context.CropTransform = cropTransform;
            context.RayDirection = view.ViewDirection.Normalize();

            foreach (Pipe pipe in pipes)
            {
                Line modelLine = GetPipeAxisLine(pipe, view, result);
                if (modelLine == null)
                {
                    context.SkippedCount++;
                    continue;
                }

                XYZ v0 = inverse.OfPoint(modelLine.GetEndPoint(0));
                XYZ v1 = inverse.OfPoint(modelLine.GetEndPoint(1));

                double dx = v1.X - v0.X;
                double dy = v1.Y - v0.Y;
                double projectedLengthMm = Math.Sqrt(dx * dx + dy * dy) / MillimetersToFeet;

                if (projectedLengthMm < MinProjectedLengthMm)
                {
                    Logger.Debug(
                        $"View '{view.Name}': pipe axis projection {projectedLengthMm:F1} mm " +
                        $"(< {MinProjectedLengthMm} mm). Skipped.");
                    context.SkippedCount++;
                    continue;
                }

                int sampleCount = (int)Math.Ceiling(projectedLengthMm / OcclusionSampleStepMm);
                sampleCount = Math.Max(MinSamples, Math.Min(MaxSamples, sampleCount));

                context.Pipes.Add(new PipeAxisData
                {
                    Pipe = pipe,
                    ModelLine = modelLine,
                    V0 = v0,
                    Dx = dx,
                    Dy = dy,
                    ProjectedLengthMm = projectedLengthMm,
                    SampleCount = sampleCount
                });
            }

            return context;
        }

        private List<ElementId> CollectRayTargets(Document doc, View view)
        {
            List<ElementId> targetIds = new List<ElementId>();

            foreach (Element element in new FilteredElementCollector(doc, view.Id))
            {
                if (element == null || !element.IsValidObject)
                {
                    continue;
                }

                if (element is CurveElement || element is TextNote || element is Dimension ||
                    element is Level || element is Grid || element is View)
                {
                    continue;
                }

                targetIds.Add(element.Id);
            }

            return targetIds;
        }

        private List<ElementId> CollectTargetPhases(Document doc, List<ElementId> targetIds)
        {
            List<ElementId> phases = new List<ElementId>();
            HashSet<ElementId> seen = new HashSet<ElementId>();

            foreach (ElementId targetId in targetIds)
            {
                ElementId phaseId = null;

                try
                {
                    phaseId = doc.GetElement(targetId)?.CreatedPhaseId;
                }
                catch (Exception)
                {
                    phaseId = null;
                }

                if (phaseId == null || phaseId == ElementId.InvalidElementId)
                {
                    continue;
                }

                if (seen.Add(phaseId))
                {
                    phases.Add(phaseId);
                }
            }

            string phaseNames = string.Join(
                ", ",
                phases.Select(p => $"'{GetElementName(doc, p)}'"));

            Logger.Info($"Target phases: {phases.Count} ({phaseNames}).");
            return phases;
        }

        private void ComputeMasksForPass(
            ViewContext context,
            ReferenceIntersector intersector,
            Dictionary<ElementId, List<ElementId>> insulations,
            OcclusionStats passStats,
            PipeAxisResult result)
        {
            foreach (PipeAxisData data in context.Pipes)
            {
                try
                {
                    if (data.Mask == null)
                    {
                        data.Mask = new List<bool>(data.SampleCount);
                        for (int i = 0; i < data.SampleCount; i++)
                        {
                            data.Mask.Add(true);
                        }
                    }

                    HashSet<ElementId> excluded = BuildExcludedIds(insulations, data.Pipe);

                    for (int i = 0; i < data.SampleCount; i++)
                    {
                        if (!data.Mask[i])
                        {
                            continue;
                        }

                        double t = (i + 0.5) / data.SampleCount;
                        XYZ worldPoint = data.ModelLine.Evaluate(t, true);

                        passStats.Rays++;
                        result.RaysCastCount++;

                        IList<ReferenceWithContext> hits = intersector.Find(worldPoint, context.RayDirection);

                        if (hits == null)
                        {
                            continue;
                        }

                        foreach (ReferenceWithContext hit in hits)
                        {
                            ElementId hitId = hit.GetReference().ElementId;
                            if (excluded.Contains(hitId))
                            {
                                continue;
                            }

                            passStats.Hits++;
                            result.HitsReceivedCount++;
                            data.Mask[i] = false;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Pipe {data.Pipe.Id}: occlusion pass failed ({ex.Message}). Remaining samples treated as visible.");
                }
            }
        }

        private void FinalizeView(Document doc, ViewContext context, GraphicsStyle lineStyle, bool occlusionEnabled, PipeAxisResult result)
        {
            int createdCount = 0;
            int occludedCount = 0;
            int partialCount = 0;

            foreach (PipeAxisData data in context.Pipes)
            {
                List<Tuple<double, double>> intervals = occlusionEnabled && data.Mask != null
                    ? MergeVisibleIntervals(data.Mask, data.SampleCount)
                    : new List<Tuple<double, double>> { Tuple.Create(0.0, 1.0) };

                if (intervals.Count == 0)
                {
                    occludedCount++;
                    continue;
                }

                int createdBefore = createdCount;

                foreach (Tuple<double, double> interval in intervals)
                {
                    double t0 = interval.Item1;
                    double t1 = interval.Item2;

                    if (data.ProjectedLengthMm * (t1 - t0) < MinProjectedLengthMm)
                    {
                        continue;
                    }

                    XYZ world0 = context.CropTransform.OfPoint(new XYZ(data.V0.X + t0 * data.Dx, data.V0.Y + t0 * data.Dy, 0.0));
                    XYZ world1 = context.CropTransform.OfPoint(new XYZ(data.V0.X + t1 * data.Dx, data.V0.Y + t1 * data.Dy, 0.0));

                    if (TryCreateAxisLine(doc, context.View, world0, world1, lineStyle, result))
                    {
                        createdCount++;
                    }
                }

                if (createdCount == createdBefore)
                {
                    context.SkippedCount++;
                    continue;
                }

                if (intervals.Count > 1)
                {
                    partialCount++;
                }
            }

            result.LinesCreatedCount += createdCount;
            result.PipesSkippedCount += context.SkippedCount;
            result.PipesFullyOccludedCount += occludedCount;
            result.PipesPartiallyVisibleCount += partialCount;

            if (context.Pipes.Count > 0 || context.DeletedCount > 0)
            {
                result.ViewsProcessedCount++;
            }

            result.Details.Add(
                $"Вид «{context.View.Name}»: труб {context.Pipes.Count}, линий создано {createdCount}, " +
                $"пропущено {context.SkippedCount}, перекрыто {occludedCount}, частично {partialCount}, " +
                $"старых удалено {context.DeletedCount}.");
            Logger.Info(
                $"View '{context.View.Name}': pipes {context.Pipes.Count}, created {createdCount}, " +
                $"skipped {context.SkippedCount}, occluded {occludedCount}, partial {partialCount}, " +
                $"deleted {context.DeletedCount}.");

            if (occlusionEnabled)
            {
                Logger.Info(
                    $"View '{context.View.Name}' occlusion diagnostics: rays {context.Stats.Rays}, hits {context.Stats.Hits}.");

                if (context.Stats.Rays > 0 && context.Stats.Hits == 0)
                {
                    Logger.Warn(
                        $"View '{context.View.Name}': rays found no obstacles at all across all phase passes — " +
                        "occlusion check may be ineffective for this view.");
                }
            }
        }

        private View3D FindAssemblyRayView(Document doc, AssemblyInstance assembly)
        {
            View candidate = new ViewService().GetViewByName(doc, assembly.Name, typeof(View3D));

            if (candidate is View3D view3D
                && view3D.IsValidObject
                && !view3D.IsTemplate
                && !view3D.IsPerspective)
            {
                return view3D;
            }

            Logger.Warn(
                $"Assembly '{assembly.Name}': 3D view not found or unsuitable for ray casting " +
                "(missing, template or perspective).");
            return null;
        }

        private void LogRayViewInfo(Document doc, AssemblyInstance assembly, View3D rayView)
        {
            string sectionBoxState;
            try
            {
                sectionBoxState = rayView.IsSectionBoxActive ? "on" : "off";
            }
            catch (Exception)
            {
                sectionBoxState = "?";
            }

            Logger.Info(
                $"Ray view for assembly '{assembly.Name}': 3D view '{rayView.Name}', " +
                $"phase '{GetPhaseName(doc, rayView)}', detail level {rayView.DetailLevel}, " +
                $"section box {sectionBoxState}.");
        }

        private bool SetViewPhase(Document doc, View3D view, ElementId phaseId)
        {
            try
            {
                Parameter parameter = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (parameter == null || parameter.IsReadOnly || phaseId == null)
                {
                    return false;
                }

                parameter.Set(phaseId);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"SetViewPhase failed: {ex.Message}");
                return false;
            }
        }

        private ElementId GetViewPhaseId(View3D view)
        {
            try
            {
                return view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private Dictionary<ElementId, List<ElementId>> CollectInsulationsByHost(Document doc)
        {
            Dictionary<ElementId, List<ElementId>> map = new Dictionary<ElementId, List<ElementId>>();

            foreach (InsulationLiningBase insulation in new FilteredElementCollector(doc)
                .OfClass(typeof(InsulationLiningBase))
                .Cast<InsulationLiningBase>())
            {
                try
                {
                    ElementId hostId = insulation.HostElementId;
                    if (hostId == null || hostId == ElementId.InvalidElementId)
                    {
                        continue;
                    }

                    if (!map.TryGetValue(hostId, out List<ElementId> list))
                    {
                        list = new List<ElementId>();
                        map[hostId] = list;
                    }

                    list.Add(insulation.Id);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Insulation {insulation.Id}: HostElementId failed: {ex.Message}");
                }
            }

            Logger.Info($"Insulation hosts collected: {map.Count}.");
            return map;
        }

        private List<Tuple<double, double>> MergeVisibleIntervals(List<bool> mask, int sampleCount)
        {
            List<Tuple<double, double>> intervals = new List<Tuple<double, double>>();
            int i = 0;

            while (i < sampleCount)
            {
                if (!mask[i])
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < sampleCount && mask[i])
                {
                    i++;
                }

                intervals.Add(Tuple.Create((double)start / sampleCount, (double)i / sampleCount));
            }

            return intervals;
        }

        private HashSet<ElementId> BuildExcludedIds(Dictionary<ElementId, List<ElementId>> insulations, Pipe pipe)
        {
            HashSet<ElementId> excluded = new HashSet<ElementId> { pipe.Id };

            if (insulations != null && insulations.TryGetValue(pipe.Id, out List<ElementId> ownInsulation))
            {
                foreach (ElementId insulationId in ownInsulation)
                {
                    excluded.Add(insulationId);
                }
            }

            return excluded;
        }

        private void RunOcclusionProbe(
            Document doc,
            View view,
            View3D rayView,
            List<ElementId> targetIds,
            Dictionary<ElementId, List<ElementId>> insulations,
            Pipe pipe,
            PipeAxisResult result,
            string passInfo)
        {
            try
            {
                if (rayView == null)
                {
                    return;
                }

                LocationCurve location = pipe.Location as LocationCurve;
                Curve curve = location?.Curve;
                if (curve == null)
                {
                    return;
                }

                XYZ origin = curve.Evaluate(0.5, true);
                XYZ forward = view.ViewDirection.Normalize();
                XYZ backward = -forward;

                Logger.Info(
                    $"Probe [{view.Name}] ({passInfo}): pipe {pipe.Id}, IsHidden(rayView) = {SafeIsHidden(pipe, rayView)}.");

                Logger.Info($"Probe [{view.Name}] ({passInfo}) unfiltered forward: {ProbeFind(doc, rayView, null, origin, forward)}");
                Logger.Info($"Probe [{view.Name}] ({passInfo}) unfiltered backward: {ProbeFind(doc, rayView, null, origin, backward)}");

                HashSet<ElementId> targets = new HashSet<ElementId>(targetIds);
                targets.Remove(pipe.Id);

                if (insulations != null && insulations.TryGetValue(pipe.Id, out List<ElementId> ownInsulation))
                {
                    foreach (ElementId insulationId in ownInsulation)
                    {
                        targets.Remove(insulationId);
                    }
                }

                Logger.Info($"Probe [{view.Name}] ({passInfo}) ray targets: {targets.Count}.");
                Logger.Info($"Probe [{view.Name}] ({passInfo}) targets forward: {ProbeFind(doc, rayView, targets.ToList(), origin, forward)}");
                Logger.Info($"Probe [{view.Name}] ({passInfo}) targets backward: {ProbeFind(doc, rayView, targets.ToList(), origin, backward)}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Probe [{view.Name}] failed: {ex.Message}");
            }
        }

        private string ProbeFind(Document doc, View3D rayView, List<ElementId> targets, XYZ origin, XYZ direction)
        {
            using (ReferenceIntersector intersector = targets == null
                ? new ReferenceIntersector(rayView)
                : new ReferenceIntersector(targets, FindReferenceTarget.Face, rayView))
            {
                IList<ReferenceWithContext> hits = intersector.Find(origin, direction);

                if (hits == null || hits.Count == 0)
                {
                    return "0 hits";
                }

                List<string> parts = new List<string>();
                foreach (ReferenceWithContext hit in hits.Take(5))
                {
                    ElementId hitId = hit.GetReference().ElementId;
                    string category = doc.GetElement(hitId)?.Category?.Name ?? "?";
                    parts.Add($"{hitId} ({category}) @ {hit.Proximity / MillimetersToFeet:F0}mm");
                }

                return $"{hits.Count} hits: {string.Join("; ", parts)}";
            }
        }

        private int DeleteAxisLines(Document doc, View view, GraphicsStyle lineStyle, PipeAxisResult result)
        {
            List<ElementId> ids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Lines)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(l => l is DetailLine)
                .Where(l => l.LineStyle != null && l.LineStyle.Id == lineStyle.Id)
                .Select(l => l.Id)
                .ToList();

            foreach (ElementId id in ids)
            {
                doc.Delete(id);
            }

            result.LinesDeletedCount += ids.Count;
            return ids.Count;
        }

        private Line GetPipeAxisLine(Pipe pipe, View view, PipeAxisResult result)
        {
            if (pipe == null || !pipe.IsValidObject)
            {
                return null;
            }

            LocationCurve location = pipe.Location as LocationCurve;
            Curve curve = location?.Curve;

            if (curve == null)
            {
                result.Warnings.Add($"Вид «{view.Name}»: у трубы id {pipe.Id} нет осевой линии — пропущена.");
                Logger.Warn($"Pipe {pipe.Id}: no location curve. Skipped.");
                return null;
            }

            Line line = curve as Line;
            if (line == null)
            {
                result.Warnings.Add($"Вид «{view.Name}»: труба id {pipe.Id} не прямая — пропущена (v1).");
                Logger.Warn($"Pipe {pipe.Id}: location curve is not a straight line. Skipped.");
                return null;
            }

            return line;
        }

        private bool TryCreateAxisLine(Document doc, View view, XYZ world0, XYZ world1, GraphicsStyle lineStyle, PipeAxisResult result)
        {
            DetailLine axis = TryNewDetailCurve(doc, view, world0, world1);

            if (axis == null && view is ViewPlan plan)
            {
                Plane plane = TryGetPlanSketchPlane(plan);
                if (plane != null)
                {
                    XYZ onPlane0 = ProjectOntoPlane(plane, world0);
                    XYZ onPlane1 = ProjectOntoPlane(plane, world1);
                    axis = TryNewDetailCurve(doc, view, onPlane0, onPlane1);
                }
            }

            if (axis == null)
            {
                result.Warnings.Add($"Вид «{view.Name}»: не удалось создать линию оси.");
                Logger.Warn($"View '{view.Name}': failed to create detail line for pipe axis.");
                return false;
            }

            axis.LineStyle = lineStyle;
            return true;
        }

        private DetailLine TryNewDetailCurve(Document doc, View view, XYZ p0, XYZ p1)
        {
            try
            {
                Line line = Line.CreateBound(p0, p1);
                return doc.Create.NewDetailCurve(view, line) as DetailLine;
            }
            catch (ArgumentException ex)
            {
                Logger.Debug($"View '{view.Name}': NewDetailCurve rejected the curve: {ex.Message}");
                return null;
            }
        }

        private Plane TryGetPlanSketchPlane(ViewPlan plan)
        {
            try
            {
                SketchPlane sketchPlane = plan.SketchPlane;
                return sketchPlane?.GetPlane();
            }
            catch (Exception ex)
            {
                Logger.Debug($"Plan '{plan?.Name}': SketchPlane is unavailable: {ex.Message}");
                return null;
            }
        }

        private static XYZ ProjectOntoPlane(Plane plane, XYZ point)
        {
            XYZ delta = point - plane.Origin;
            double signedDistance = delta.DotProduct(plane.Normal);
            return point - signedDistance * plane.Normal;
        }

        private bool SafeIsHidden(Element element, View view)
        {
            try
            {
                return element.IsHidden(view);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetElementName(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId)
            {
                return "?";
            }

            return doc.GetElement(id)?.Name ?? "?";
        }

        private static string GetPhaseName(Document doc, View view)
        {
            try
            {
                return GetElementName(doc, view.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId());
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string GetPhaseFilterName(Document doc, View view)
        {
            try
            {
                return GetElementName(doc, view.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER)?.AsElementId());
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string GetStyleName(GraphicsStyle lineStyle)
        {
            return lineStyle?.GraphicsStyleCategory?.Name ?? lineStyle?.Name ?? "?";
        }
    }
}
