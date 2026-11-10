using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Common;

namespace AssemblingManager.Revit.Services
{
    public class SheetElevationCheckItem
    {
        public ElementId SheetId { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public int PlacedViewCount { get; set; }
        public string ViewsText { get; set; }
    }

    public class ElevationCheckResult
    {
        public int CheckedSheetCount { get; set; }
        public int SheetsWithElevationsCount { get; set; }
        public int SheetsWithoutAnyViewsCount { get; set; }
        public int SkippedSheetCount { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<SheetElevationCheckItem> ProblemSheets { get; set; }
    }

    /// <summary>
    /// Проверяет, есть ли на размещённых видах листа системная аннотация «Высотная отметка».
    /// Операция только читает документ и не требует транзакции.
    /// </summary>
    public class ElevationCheckService
    {
        private static readonly ElementId SpotElevationCategoryId =
            new ElementId(BuiltInCategory.OST_SpotElevations);

        private static readonly NaturalStringComparer SheetNumberComparer = new NaturalStringComparer();

        public ElevationCheckResult Check(Document document, IEnumerable<ElementId> sheetIds)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Dictionary<ElementId, List<Element>> elevationsByView = CollectElevationsByView(document);
            Logger.Info($"ElevationCheckService: spot elevations found in {elevationsByView.Count} views.");

            ElevationCheckResult result = new ElevationCheckResult
            {
                ProblemSheets = new List<SheetElevationCheckItem>()
            };

            HashSet<ElementId> visitedSheets = new HashSet<ElementId>();

            foreach (ElementId sheetId in sheetIds ?? Enumerable.Empty<ElementId>())
            {
                if (sheetId == null || sheetId == ElementId.InvalidElementId || !visitedSheets.Add(sheetId))
                {
                    continue;
                }

                ViewSheet sheet = document.GetElement(sheetId) as ViewSheet;
                if (sheet == null || sheet.IsTemplate)
                {
                    result.SkippedSheetCount++;
                    continue;
                }

                result.CheckedSheetCount++;

                List<View> placedViews = GetPlacedViews(document, sheet);
                if (placedViews.Count == 0)
                {
                    result.SheetsWithoutAnyViewsCount++;
                }

                if (HasElevationOnAnyView(document, placedViews, elevationsByView))
                {
                    result.SheetsWithElevationsCount++;
                    continue;
                }

                result.ProblemSheets.Add(new SheetElevationCheckItem
                {
                    SheetId = sheet.Id,
                    SheetNumber = (sheet.SheetNumber ?? string.Empty).Trim(),
                    SheetName = SheetService.GetSheetName(sheet),
                    PlacedViewCount = placedViews.Count,
                    ViewsText = BuildViewsText(placedViews)
                });
            }

            result.ProblemSheets = result.ProblemSheets
                .OrderBy(item => item.SheetNumber, SheetNumberComparer)
                .ThenBy(item => item.SheetName, SheetNumberComparer)
                .ToList();

            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;

            Logger.Info(
                $"ElevationCheckService: checked {result.CheckedSheetCount} sheets, " +
                $"{result.SheetsWithElevationsCount} with elevations, " +
                $"{result.ProblemSheets.Count} without elevations, " +
                $"{result.SheetsWithoutAnyViewsCount} without placed views, " +
                $"{result.SkippedSheetCount} skipped, {result.Elapsed.TotalSeconds:F2} s.");

            return result;
        }

        private static Dictionary<ElementId, List<Element>> CollectElevationsByView(Document document)
        {
            Dictionary<ElementId, List<Element>> result = new Dictionary<ElementId, List<Element>>();

            IEnumerable<Element> elevations = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_SpotElevations)
                .WhereElementIsNotElementType();

            foreach (Element elevation in elevations)
            {
                ElementId viewId = elevation.OwnerViewId;
                if (viewId == null || viewId == ElementId.InvalidElementId)
                {
                    continue;
                }

                if (!result.TryGetValue(viewId, out List<Element> viewElevations))
                {
                    viewElevations = new List<Element>();
                    result[viewId] = viewElevations;
                }

                viewElevations.Add(elevation);
            }

            return result;
        }

        private static List<View> GetPlacedViews(Document document, ViewSheet sheet)
        {
            List<View> views = new List<View>();

            foreach (ElementId viewId in sheet.GetAllPlacedViews())
            {
                View view = document.GetElement(viewId) as View;
                if (view != null && !view.IsTemplate)
                {
                    views.Add(view);
                }
            }

            return views;
        }

        private static bool HasElevationOnAnyView(
            Document document,
            IReadOnlyCollection<View> placedViews,
            IReadOnlyDictionary<ElementId, List<Element>> elevationsByView)
        {
            foreach (View view in placedViews)
            {
                if (HasElevationInView(document, view, elevationsByView))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasElevationInView(
            Document document,
            View view,
            IReadOnlyDictionary<ElementId, List<Element>> elevationsByView)
        {
            if (IsCategoryHidden(view))
            {
                return false;
            }

            if (elevationsByView.TryGetValue(view.Id, out List<Element> ownElevations)
                && ownElevations.Count > 0
                && ownElevations.Any(elevation => IsElevationVisible(elevation, view)))
            {
                return true;
            }

            // У зависимого вида Revit указывает владельцем аннотаций основной вид.
            // Поэтому для зависимого вида дополнительно спрашиваем сам вид, что на нём нарисовано.
            ElementId primaryViewId = view.GetPrimaryViewId();
            if (primaryViewId == null
                || primaryViewId == ElementId.InvalidElementId
                || !elevationsByView.ContainsKey(primaryViewId))
            {
                return false;
            }

            try
            {
                return new FilteredElementCollector(document, view.Id)
                    .OfCategory(BuiltInCategory.OST_SpotElevations)
                    .WhereElementIsNotElementType()
                    .GetElementCount() > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"ElevationCheckService: could not collect elevations in dependent view '{view.Name}': {ex.Message}");
                return false;
            }
        }

        private static bool IsElevationVisible(Element elevation, View view)
        {
            if (elevation == null || !elevation.IsValidObject)
            {
                return false;
            }

            try
            {
                return !elevation.IsHidden(view);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"ElevationCheckService: could not check visibility of elevation {elevation.Id} " +
                    $"in view '{view.Name}': {ex.Message}");
                return false;
            }
        }

        private static bool IsCategoryHidden(View view)
        {
            try
            {
                return view.GetCategoryHidden(SpotElevationCategoryId);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ElevationCheckService: could not read category visibility in view '{view.Name}': {ex.Message}");
                return false;
            }
        }

        private static string BuildViewsText(IReadOnlyCollection<View> views)
        {
            const int maxNames = 15;

            if (views.Count == 0)
            {
                return "На листе нет размещённых видов.";
            }

            List<string> names = views
                .Select(view => string.IsNullOrWhiteSpace(view.Name) ? "Без имени" : view.Name)
                .OrderBy(name => name, SheetNumberComparer)
                .Take(maxNames)
                .ToList();

            string text = string.Join("; ", names);
            if (views.Count > maxNames)
            {
                text += $" и ещё {views.Count - maxNames}";
            }

            return text;
        }
    }
}
