using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public enum ViewPlacementRole
    {
        Top,
        Right,
        Bottom,
        Left,
        Unsupported
    }

    public class ObjectViewGroup
    {
        public string ObjectName { get; set; }
        public List<View> Plans { get; }
        public List<View> Sections { get; }
        public List<View> Views3D { get; }
        public List<View> Schedules { get; }
        public List<View> Unsupported { get; }

        public ObjectViewGroup()
        {
            Plans = new List<View>();
            Sections = new List<View>();
            Views3D = new List<View>();
            Schedules = new List<View>();
            Unsupported = new List<View>();
        }
    }

    public class PlacedItem
    {
        public ElementId InstanceId { get; set; }
        public ElementId ViewId { get; set; }
        public ViewPlacementRole Role { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public XYZ CreationCenter { get; set; }
        public XYZ ActualCenter { get; set; }
        public XYZ TargetCenter { get; set; }
    }

    public class SheetService
    {
        private const double MillimetersToFeet = 1.0 / 304.8;
        private const double GapFromSheetEdgeMm = 25.0;
        private const double GapBetweenItemsMm = 50.0;
        private const double DefaultSizeFraction = 0.35;

        private static readonly string[] SectionSuffixPriority =
        {
            ViewService.FrontViewSuffix,
            ViewService.BackViewSuffix,
            ViewService.RightViewSuffix,
            ViewService.LeftViewSuffix
        };

        public List<ViewSheet> GetSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public HashSet<string> GetAllSheetNumbers(Document doc)
        {
            HashSet<string> numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ViewSheet sheet in GetSheets(doc))
            {
                numbers.Add(sheet.SheetNumber.Trim());
            }

            return numbers;
        }

        public HashSet<ElementId> GetViewsPlacedOnSheets(Document doc)
        {
            HashSet<ElementId> placed = new HashSet<ElementId>();

            foreach (Viewport viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                placed.Add(viewport.ViewId);
            }

            foreach (ScheduleSheetInstance instance in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                placed.Add(instance.ScheduleId);
            }

            return placed;
        }

        public List<ObjectViewGroup> GroupViewsByObject(Document doc, IList<ElementId> viewIds)
        {
            Dictionary<string, ObjectViewGroup> groups = new Dictionary<string, ObjectViewGroup>(StringComparer.Ordinal);

            foreach (ElementId viewId in viewIds)
            {
                View view = doc.GetElement(viewId) as View;
                if (view == null || view.IsTemplate)
                {
                    continue;
                }

                string objectName = ParseBaseName(view.Name);
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    continue;
                }

                ObjectViewGroup group;
                if (!groups.TryGetValue(objectName, out group))
                {
                    group = new ObjectViewGroup { ObjectName = objectName };
                    groups[objectName] = group;
                }

                if (view is ViewPlan)
                {
                    group.Plans.Add(view);
                }
                else if (view is View3D)
                {
                    group.Views3D.Add(view);
                }
                else if (view is ViewSection)
                {
                    group.Sections.Add(view);
                }
                else if (view is ViewSchedule)
                {
                    group.Schedules.Add(view);
                }
                else
                {
                    group.Unsupported.Add(view);
                }
            }

            foreach (ObjectViewGroup group in groups.Values)
            {
                ViewSchedule schedule = FindScheduleByName(doc, group.ObjectName + ScheduleService.ScheduleSuffix);
                if (schedule != null)
                {
                    group.Schedules.Add(schedule);
                }

                group.Plans.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                group.Views3D.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                group.Schedules.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                group.Sections.Sort(CompareSections);
            }

            return groups.Values
                .OrderBy(g => g.ObjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string ParseBaseName(string viewName)
        {
            string[] suffixes =
            {
                ScheduleService.ScheduleSuffix,
                ViewService.View3DSuffix,
                ViewService.PlanSuffix,
                ViewService.FrontViewSuffix,
                ViewService.BackViewSuffix,
                ViewService.RightViewSuffix,
                ViewService.LeftViewSuffix
            };

            foreach (string suffix in suffixes)
            {
                if (string.IsNullOrEmpty(suffix))
                {
                    continue;
                }

                if (viewName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return viewName.Substring(0, viewName.Length - suffix.Length).TrimEnd();
                }
            }

            return viewName;
        }

        public string GenerateNextSheetNumber(ViewSheet master, HashSet<string> occupiedNumbers)
        {
            string masterNumber = master.SheetNumber != null ? master.SheetNumber.Trim() : string.Empty;
            Match match = Regex.Match(masterNumber, @"^(.*?)(\d+)$");

            if (match.Success)
            {
                string prefix = match.Groups[1].Value;
                string digits = match.Groups[2].Value;

                long value;
                if (!long.TryParse(digits, out value))
                {
                    value = 0;
                }

                bool hasLeadingZeros = digits.Length > 1 && digits.StartsWith("0");

                for (int i = 0; i < 100000; i++)
                {
                    value++;
                    string digitsText = hasLeadingZeros
                        ? value.ToString().PadLeft(digits.Length, '0')
                        : value.ToString();
                    string candidate = prefix + digitsText;

                    if (!occupiedNumbers.Contains(candidate))
                    {
                        occupiedNumbers.Add(candidate);
                        return candidate;
                    }
                }
            }
            else
            {
                for (int i = 1; i < 100000; i++)
                {
                    string candidate = $"{masterNumber}-{i:00}";

                    if (!occupiedNumbers.Contains(candidate))
                    {
                        occupiedNumbers.Add(candidate);
                        return candidate;
                    }
                }
            }

            throw new InvalidOperationException($"Не удалось подобрать свободный номер листа для образца «{masterNumber}».");
        }

        public ViewSheet CreateSheetCopy(Document doc, ViewSheet master, string objectName, string newNumber, IList<ElementId> groupParameterIds)
        {
            ElementId titleBlockTypeId = GetTitleBlockTypeId(doc, master);

            ViewSheet sheet = ViewSheet.Create(doc, titleBlockTypeId);
            TrySetName(sheet, objectName);

            Parameter numberParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
            if (numberParameter != null && !numberParameter.IsReadOnly)
            {
                numberParameter.Set(newNumber);
            }

            CopyParameterValues(master, sheet, groupParameterIds);

            return sheet;
        }

        public List<ElementId> GetSheetGroupParameterIds(Document doc, ViewSheet masterSheet)
        {
            List<ElementId> parameterIds = new List<ElementId>();

            try
            {
                BrowserOrganization organization = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
                IList<FolderItemInfo> folderItems = organization.GetFolderItems(masterSheet.Id);

                if (folderItems == null)
                {
                    return parameterIds;
                }

                foreach (FolderItemInfo folderItem in folderItems)
                {
                    if (folderItem.ElementId != null
                        && folderItem.ElementId != ElementId.InvalidElementId
                        && !parameterIds.Contains(folderItem.ElementId))
                    {
                        parameterIds.Add(folderItem.ElementId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read sheets browser organization: {ex.Message}");
            }

            Logger.Info($"Sheet group parameters to copy: {parameterIds.Count}.");

            return parameterIds;
        }

        private static void CopyParameterValues(ViewSheet source, ViewSheet target, IList<ElementId> parameterIds)
        {
            if (parameterIds == null)
            {
                return;
            }

            foreach (ElementId parameterId in parameterIds)
            {
                Parameter sourceParameter = FindParameterById(source, parameterId);
                Parameter targetParameter = FindParameterById(target, parameterId);

                if (sourceParameter == null || targetParameter == null || targetParameter.IsReadOnly || !sourceParameter.HasValue)
                {
                    continue;
                }

                try
                {
                    switch (sourceParameter.StorageType)
                    {
                        case StorageType.String:
                            targetParameter.Set(sourceParameter.AsString());
                            break;
                        case StorageType.Integer:
                            targetParameter.Set(sourceParameter.AsInteger());
                            break;
                        case StorageType.Double:
                            targetParameter.Set(sourceParameter.AsDouble());
                            break;
                        case StorageType.ElementId:
                            ElementId value = sourceParameter.AsElementId();
                            if (value != null)
                            {
                                targetParameter.Set(value);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not copy parameter {parameterId} to sheet: {ex.Message}");
                }
            }
        }

        private static Parameter FindParameterById(Element element, ElementId parameterId)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.Id == parameterId)
                {
                    return parameter;
                }
            }

            return null;
        }

        public static string GetSheetName(ViewSheet sheet)
        {
            Parameter nameParameter = sheet.get_Parameter(BuiltInParameter.SHEET_NAME);
            string name = nameParameter != null ? nameParameter.AsString() : null;

            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            return sheet.Name;
        }

        public List<SheetConflictItem> FindSheetConflicts(Document doc, List<ObjectViewGroup> objects, ViewSheet masterSheet)
        {
            List<ViewSheet> allSheets = GetSheets(doc);
            List<SheetConflictItem> conflicts = new List<SheetConflictItem>();

            foreach (ObjectViewGroup group in objects)
            {
                List<ViewSheet> existing = FindSheetsByName(allSheets, group.ObjectName, masterSheet);

                if (existing.Count == 0)
                {
                    continue;
                }

                conflicts.Add(new SheetConflictItem
                {
                    ObjectName = group.ObjectName,
                    SheetNumber = existing[0].SheetNumber,
                    SheetName = GetSheetName(existing[0]),
                    DuplicatesCount = existing.Count - 1,
                    Replace = false
                });
            }

            return conflicts;
        }

        public List<ViewSheet> FindSheetsByName(Document doc, string name, ViewSheet masterSheet)
        {
            return FindSheetsByName(GetSheets(doc), name, masterSheet);
        }

        private static List<ViewSheet> FindSheetsByName(List<ViewSheet> sheets, string name, ViewSheet masterSheet)
        {
            return sheets
                .Where(s => s.Id != masterSheet.Id
                            && string.Equals(GetSheetName(s).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public void PrepareSheetsForPlacement(
            Document doc,
            ViewSheet targetSheet,
            List<ViewSheet> duplicateSheets,
            ObjectViewGroup group,
            HashSet<ElementId> alreadyPlacedViewIds,
            Dictionary<ElementId, XYZ> positionHints,
            IList<string> warnings)
        {
            HashSet<ElementId> currentViewIds = GetObjectViewIds(group);

            List<ViewSheet> sheetsToClean = new List<ViewSheet>();
            sheetsToClean.Add(targetSheet);
            sheetsToClean.AddRange(duplicateSheets);

            foreach (ViewSheet sheet in sheetsToClean)
            {
                bool isTarget = sheet.Id == targetSheet.Id;
                List<ElementId> toDelete = new List<ElementId>();

                foreach (Viewport viewport in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                {
                    View view = doc.GetElement(viewport.ViewId) as View;
                    if (view == null || ParseBaseName(view.Name) != group.ObjectName)
                    {
                        continue;
                    }

                    if (currentViewIds.Contains(view.Id))
                    {
                        if (isTarget)
                        {
                            alreadyPlacedViewIds.Add(view.Id);
                        }
                        else
                        {
                            positionHints[view.Id] = GetElementOnSheetCenter(doc, sheet, viewport);
                            toDelete.Add(viewport.Id);
                            alreadyPlacedViewIds.Remove(view.Id);
                        }
                    }
                    else
                    {
                        toDelete.Add(viewport.Id);
                    }
                }

                foreach (ScheduleSheetInstance instance in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                {
                    View schedule = doc.GetElement(instance.ScheduleId) as View;
                    if (schedule == null || ParseBaseName(schedule.Name) != group.ObjectName)
                    {
                        continue;
                    }

                    if (currentViewIds.Contains(schedule.Id))
                    {
                        if (isTarget)
                        {
                            alreadyPlacedViewIds.Add(schedule.Id);
                        }
                        else
                        {
                            positionHints[schedule.Id] = GetElementOnSheetCenter(doc, sheet, instance);
                            toDelete.Add(instance.Id);
                            alreadyPlacedViewIds.Remove(schedule.Id);
                        }
                    }
                    else
                    {
                        toDelete.Add(instance.Id);
                    }
                }

                if (toDelete.Count > 0)
                {
                    doc.Delete(toDelete);
                    Logger.Info($"Cleaned {toDelete.Count} pattern viewports from sheet '{sheet.SheetNumber}'.");
                }
            }
        }

        public static HashSet<ElementId> GetObjectViewIds(ObjectViewGroup group)
        {
            HashSet<ElementId> ids = new HashSet<ElementId>();

            foreach (View view in group.Plans.Concat(group.Views3D).Concat(group.Sections).Concat(group.Schedules))
            {
                ids.Add(view.Id);
            }

            return ids;
        }

        private static XYZ GetElementOnSheetCenter(Document doc, ViewSheet sheet, Element element)
        {
            BoundingBoxXYZ bbox = element.get_BoundingBox(sheet);

            if (bbox != null && bbox.Min != null && bbox.Max != null)
            {
                return new XYZ(
                    (bbox.Min.X + bbox.Max.X) / 2.0,
                    (bbox.Min.Y + bbox.Max.Y) / 2.0,
                    0.0);
            }

            Viewport viewport = element as Viewport;
            if (viewport != null)
            {
                Outline outline = viewport.GetBoxOutline();
                return new XYZ(
                    (outline.MinimumPoint.X + outline.MaximumPoint.X) / 2.0,
                    (outline.MinimumPoint.Y + outline.MaximumPoint.Y) / 2.0,
                    0.0);
            }

            return null;
        }

        public void PlaceObjectViewsOnSheet(Document doc, ViewSheet sheet, ObjectViewGroup group, HashSet<ElementId> alreadyPlacedViewIds, Dictionary<ElementId, XYZ> positionHints, IList<string> warnings)
        {
            List<PlacedItem> items = new List<PlacedItem>();

            items.AddRange(CreateViewports(doc, sheet, group.Plans, ViewPlacementRole.Top, alreadyPlacedViewIds, warnings));
            items.AddRange(CreateViewports(doc, sheet, group.Views3D, ViewPlacementRole.Right, alreadyPlacedViewIds, warnings));
            items.AddRange(CreateViewports(doc, sheet, group.Sections, ViewPlacementRole.Left, alreadyPlacedViewIds, warnings));
            items.AddRange(CreateScheduleInstances(doc, sheet, group.Schedules, alreadyPlacedViewIds, warnings));

            foreach (View view in group.Unsupported)
            {
                warnings.Add($"«{view.Name}»: тип вида не поддерживается (размещаются планы, разрезы, 3D и спецификации).");
            }

            if (items.Count == 0)
            {
                return;
            }

            doc.Regenerate();
            MeasureItems(doc, sheet, items);

            BoundingBoxUV sheetOutline = sheet.Outline;
            double minX = sheetOutline.Min.U;
            double maxX = sheetOutline.Max.U;
            double minY = sheetOutline.Min.V;
            double maxY = sheetOutline.Max.V;

            double gap = GapFromSheetEdgeMm * MillimetersToFeet;
            double itemGap = GapBetweenItemsMm * MillimetersToFeet;

            LayoutRow(items.Where(i => i.Role == ViewPlacementRole.Top).ToList(), minX, maxX, maxY, gap, itemGap, alignTop: true);
            LayoutRow(items.Where(i => i.Role == ViewPlacementRole.Bottom).ToList(), minX, maxX, minY, gap, itemGap, alignTop: false);
            LayoutColumn(items.Where(i => i.Role == ViewPlacementRole.Left).ToList(), minY, maxY, minX, gap, itemGap, isLeft: true);
            LayoutColumn(items.Where(i => i.Role == ViewPlacementRole.Right).ToList(), minY, maxY, maxX, gap, itemGap, isLeft: false);

            if (positionHints != null)
            {
                foreach (PlacedItem item in items)
                {
                    XYZ hint;
                    if (item.ViewId != null && positionHints.TryGetValue(item.ViewId, out hint) && hint != null)
                    {
                        item.TargetCenter = hint;
                    }
                }
            }

            foreach (PlacedItem item in items)
            {
                XYZ delta = item.TargetCenter - item.ActualCenter;
                if (delta.GetLength() > 1e-9)
                {
                    ElementTransformUtils.MoveElement(doc, item.InstanceId, delta);
                }
            }
        }

        private IEnumerable<PlacedItem> CreateViewports(Document doc, ViewSheet sheet, IEnumerable<View> views, ViewPlacementRole role, HashSet<ElementId> alreadyPlacedViewIds, IList<string> warnings)
        {
            List<PlacedItem> items = new List<PlacedItem>();

            BoundingBoxUV sheetOutline = sheet.Outline;
            XYZ sheetCenter = new XYZ(
                (sheetOutline.Min.U + sheetOutline.Max.U) / 2.0,
                (sheetOutline.Min.V + sheetOutline.Max.V) / 2.0,
                0.0);
            double sheetWidth = sheetOutline.Max.U - sheetOutline.Min.U;
            double sheetHeight = sheetOutline.Max.V - sheetOutline.Min.V;

            foreach (View view in views)
            {
                if (alreadyPlacedViewIds.Contains(view.Id))
                {
                    warnings.Add($"«{view.Name}»: вид уже размещён на листе, пропущен.");
                    continue;
                }

                try
                {
                    Viewport viewport = Viewport.Create(doc, sheet.Id, view.Id, sheetCenter);

                    PlacedItem item = new PlacedItem
                    {
                        InstanceId = viewport.Id,
                        ViewId = view.Id,
                        Role = role,
                        CreationCenter = sheetCenter
                    };
                    EstimateViewportSize(view, sheetWidth, sheetHeight, item);

                    items.Add(item);
                    alreadyPlacedViewIds.Add(view.Id);
                }
                catch (Exception ex)
                {
                    warnings.Add($"«{view.Name}»: не удалось разместить ({ex.Message}).");
                }
            }

            return items;
        }

        private IEnumerable<PlacedItem> CreateScheduleInstances(Document doc, ViewSheet sheet, IEnumerable<View> views, HashSet<ElementId> alreadyPlacedViewIds, IList<string> warnings)
        {
            List<PlacedItem> items = new List<PlacedItem>();

            BoundingBoxUV sheetOutline = sheet.Outline;
            XYZ sheetCenter = new XYZ(
                (sheetOutline.Min.U + sheetOutline.Max.U) / 2.0,
                (sheetOutline.Min.V + sheetOutline.Max.V) / 2.0,
                0.0);

            foreach (View view in views)
            {
                if (alreadyPlacedViewIds.Contains(view.Id))
                {
                    warnings.Add($"«{view.Name}»: спецификация уже размещена на листе, пропущена.");
                    continue;
                }

                try
                {
                    ScheduleSheetInstance instance = ScheduleSheetInstance.Create(doc, sheet.Id, view.Id, sheetCenter);

                    PlacedItem item = new PlacedItem
                    {
                        InstanceId = instance.Id,
                        ViewId = view.Id,
                        Role = ViewPlacementRole.Bottom,
                        CreationCenter = sheetCenter,
                        Width = (sheetOutline.Max.U - sheetOutline.Min.U) * DefaultSizeFraction,
                        Height = (sheetOutline.Max.V - sheetOutline.Min.V) * DefaultSizeFraction
                    };

                    items.Add(item);
                    alreadyPlacedViewIds.Add(view.Id);
                }
                catch (Exception ex)
                {
                    warnings.Add($"«{view.Name}»: не удалось разместить спецификацию ({ex.Message}).");
                }
            }

            return items;
        }

        private void EstimateViewportSize(View view, double sheetWidth, double sheetHeight, PlacedItem item)
        {
            if (view is View3D || !view.CropBoxActive || view.CropBox == null)
            {
                item.Width = sheetWidth * DefaultSizeFraction;
                item.Height = sheetHeight * DefaultSizeFraction;
                return;
            }

            BoundingBoxXYZ cropBox = view.CropBox;
            Transform transform = cropBox.Transform;
            XYZ min = transform.OfPoint(cropBox.Min);
            XYZ max = transform.OfPoint(cropBox.Max);

            double modelWidth = Math.Abs(max.X - min.X);
            double modelHeight = Math.Abs(max.Y - min.Y);

            double scale = view.Scale;
            if (scale <= 0)
            {
                scale = 100.0;
            }

            item.Width = modelWidth / scale;
            item.Height = modelHeight / scale;

            if (item.Width <= 0 || item.Height <= 0)
            {
                item.Width = sheetWidth * DefaultSizeFraction;
                item.Height = sheetHeight * DefaultSizeFraction;
            }
        }

        private void MeasureItems(Document doc, ViewSheet sheet, List<PlacedItem> items)
        {
            foreach (PlacedItem item in items)
            {
                Element instance = doc.GetElement(item.InstanceId);
                BoundingBoxXYZ bbox = instance != null ? instance.get_BoundingBox(sheet) : null;

                if (bbox != null && bbox.Min != null && bbox.Max != null)
                {
                    item.Width = Math.Abs(bbox.Max.X - bbox.Min.X);
                    item.Height = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                    item.ActualCenter = new XYZ(
                        (bbox.Min.X + bbox.Max.X) / 2.0,
                        (bbox.Min.Y + bbox.Max.Y) / 2.0,
                        0.0);
                    continue;
                }

                Viewport viewport = instance as Viewport;
                if (viewport != null)
                {
                    Outline boxOutline = viewport.GetBoxOutline();
                    XYZ outlineMin = boxOutline.MinimumPoint;
                    XYZ outlineMax = boxOutline.MaximumPoint;
                    item.Width = Math.Abs(outlineMax.X - outlineMin.X);
                    item.Height = Math.Abs(outlineMax.Y - outlineMin.Y);
                    item.ActualCenter = new XYZ(
                        (outlineMin.X + outlineMax.X) / 2.0,
                        (outlineMin.Y + outlineMax.Y) / 2.0,
                        0.0);
                    continue;
                }

                item.ActualCenter = item.CreationCenter;
            }

            foreach (PlacedItem item in items)
            {
                if (item.Width <= 0)
                {
                    item.Width = 0.1;
                }

                if (item.Height <= 0)
                {
                    item.Height = 0.1;
                }
            }
        }

        private static void LayoutRow(List<PlacedItem> items, double minX, double maxX, double edge, double gap, double itemGap, bool alignTop)
        {
            if (items.Count == 0)
            {
                return;
            }

            double totalWidth = items.Sum(i => i.Width) + itemGap * (items.Count - 1);
            double x = (minX + maxX) / 2.0 - totalWidth / 2.0;

            foreach (PlacedItem item in items)
            {
                double y = alignTop
                    ? edge + gap + item.Height / 2.0
                    : edge - gap - item.Height / 2.0;

                item.TargetCenter = new XYZ(x + item.Width / 2.0, y, 0.0);
                x += item.Width + itemGap;
            }
        }

        private static void LayoutColumn(List<PlacedItem> items, double minY, double maxY, double edge, double gap, double itemGap, bool isLeft)
        {
            if (items.Count == 0)
            {
                return;
            }

            double totalHeight = items.Sum(i => i.Height) + itemGap * (items.Count - 1);
            double y = (minY + maxY) / 2.0 + totalHeight / 2.0;

            foreach (PlacedItem item in items)
            {
                double x = isLeft
                    ? edge - gap - item.Width / 2.0
                    : edge + gap + item.Width / 2.0;

                item.TargetCenter = new XYZ(x, y - item.Height / 2.0, 0.0);
                y -= item.Height + itemGap;
            }
        }

        private static int CompareSections(View a, View b)
        {
            int indexA = IndexOfSectionPriority(a.Name);
            int indexB = IndexOfSectionPriority(b.Name);

            if (indexA != indexB)
            {
                return indexA.CompareTo(indexB);
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static int IndexOfSectionPriority(string viewName)
        {
            for (int i = 0; i < SectionSuffixPriority.Length; i++)
            {
                if (viewName.EndsWith(SectionSuffixPriority[i], StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return SectionSuffixPriority.Length;
        }

        private ViewSchedule FindScheduleByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => !s.IsTemplate && s.Name == name);
        }

        private ElementId GetTitleBlockTypeId(Document doc, ViewSheet master)
        {
            FamilyInstance masterTitleBlock = new FilteredElementCollector(doc, master.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            if (masterTitleBlock != null)
            {
                return masterTitleBlock.GetTypeId();
            }

            ElementId fallbackTypeId = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Select(s => s.Id)
                .FirstOrDefault();

            if (fallbackTypeId == null)
            {
                throw new InvalidOperationException("В модели нет типов основной надписи (штампа), невозможно создать лист.");
            }

            return fallbackTypeId;
        }

        private static void TrySetName(ViewSheet sheet, string objectName)
        {
            string name = objectName;

            for (int attempt = 1; attempt <= 50; attempt++)
            {
                try
                {
                    sheet.Name = name;
                    return;
                }
                catch (ArgumentException)
                {
                    name = $"{objectName} ({attempt + 1})";
                }
            }
        }
    }
}
