using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Common;

namespace AssemblingManager.Revit.Services
{
    public class ViewGroupNode
    {
        public string Name { get; }
        public IList<ViewGroupNode> Children { get; }
        public List<ElementId> ViewIds { get; }

        public bool IsLeaf
        {
            get { return Children == null || Children.Count == 0; }
        }

        public string DisplayName
        {
            get { return IsLeaf ? $"{Name} ({ViewIds.Count})" : Name; }
        }

        public ViewGroupNode(string name, List<ElementId> viewIds)
        {
            Name = name;
            ViewIds = viewIds ?? new List<ElementId>();
            Children = new List<ViewGroupNode>();
        }

        public List<ElementId> CollectViewIds()
        {
            List<ElementId> result = new List<ElementId>(ViewIds);
            foreach (ViewGroupNode child in Children)
            {
                result.AddRange(child.CollectViewIds());
            }
            return result;
        }
    }

    public class SheetGroupNode
    {
        public string Name { get; }
        public IList<SheetGroupNode> Children { get; }
        public ViewSheet Sheet { get; }
        public SheetGroupNode Parent { get; set; }

        public bool IsSheet
        {
            get { return Sheet != null; }
        }

        public List<ViewSheet> GetAllSheets()
        {
            List<ViewSheet> sheets = new List<ViewSheet>();

            if (IsSheet && Sheet != null)
            {
                sheets.Add(Sheet);
            }

            foreach (SheetGroupNode child in Children)
            {
                sheets.AddRange(child.GetAllSheets());
            }

            return sheets;
        }

        public string DisplayName
        {
            get
            {
                if (IsSheet)
                {
                    return $"{Sheet.SheetNumber} — {SheetService.GetSheetName(Sheet)}";
                }

                return $"{Name} ({CountSheets()})";
            }
        }

        public SheetGroupNode(string name)
        {
            Name = name;
            Children = new List<SheetGroupNode>();
        }

        public SheetGroupNode(ViewSheet sheet)
        {
            Children = new List<SheetGroupNode>();
            Sheet = sheet;
        }

        public int CountSheets()
        {
            int count = IsSheet ? 1 : 0;

            foreach (SheetGroupNode child in Children)
            {
                count += child.CountSheets();
            }

            return count;
        }
    }

    public class ScheduleGroupNode
    {
        public string Name { get; }
        public IList<ScheduleGroupNode> Children { get; }
        public ViewSchedule Schedule { get; }
        public ScheduleGroupNode Parent { get; set; }

        public bool IsSchedule
        {
            get { return Schedule != null; }
        }

        public List<ViewSchedule> GetAllSchedules()
        {
            List<ViewSchedule> schedules = new List<ViewSchedule>();

            if (IsSchedule && Schedule != null)
            {
                schedules.Add(Schedule);
            }

            foreach (ScheduleGroupNode child in Children)
            {
                schedules.AddRange(child.GetAllSchedules());
            }

            return schedules;
        }

        public string DisplayName
        {
            get
            {
                if (IsSchedule)
                {
                    return Schedule.Name;
                }

                return $"{Name} ({CountSchedules()})";
            }
        }

        public ScheduleGroupNode(string name)
        {
            Name = name;
            Children = new List<ScheduleGroupNode>();
        }

        public ScheduleGroupNode(ViewSchedule schedule)
        {
            Children = new List<ScheduleGroupNode>();
            Schedule = schedule;
        }

        public int CountSchedules()
        {
            int count = IsSchedule ? 1 : 0;

            foreach (ScheduleGroupNode child in Children)
            {
                count += child.CountSchedules();
            }

            return count;
        }
    }

    public class BrowserGroupService
    {
        private static readonly NaturalStringComparer NameComparer = new NaturalStringComparer();
        private const string EmptyValueName = "(без группы)";
        private const string UnknownFolderName = "???";

        public List<ViewGroupNode> BuildGroupTree(Document doc)
        {
            BrowserOrganization organization = BrowserOrganization.GetCurrentBrowserOrganizationForViews(doc);

            List<View> views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Where(v => !(v is ViewSheet))
                .Where(v => !(v is ViewSchedule))
                .ToList();

            Logger.Info($"BrowserGroupService: found {views.Count} browser views.");

            GroupNodeBuilder root = new GroupNodeBuilder(string.Empty);

            foreach (View view in views)
            {
                List<string> path = GetFolderPath(organization, view);
                GroupNodeBuilder current = root;

                foreach (string segment in path)
                {
                    GroupNodeBuilder next;
                    if (!current.Children.TryGetValue(segment, out next))
                    {
                        next = new GroupNodeBuilder(segment);
                        current.Children[segment] = next;
                    }
                    current = next;
                }

                current.ViewIds.Add(view.Id);
            }

            return ConvertToNodes(root.Children);
        }

        private List<string> GetFolderPath(BrowserOrganization organization, View view)
        {
            IList<FolderItemInfo> folderItems;

            try
            {
                folderItems = organization.GetFolderItems(view.Id);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not get folder path for view '{view.Name}': {ex.Message}");
                return new List<string> { EmptyValueName };
            }

            if (folderItems == null || folderItems.Count == 0)
            {
                return new List<string> { EmptyValueName };
            }

            if (folderItems.Count == 1)
            {
                FolderItemInfo single = folderItems[0];

                if (single.ElementId == null || single.ElementId == ElementId.InvalidElementId)
                {
                    return new List<string> { EmptyValueName };
                }

                return new List<string> { GetFolderName(single) };
            }

            List<string> path = new List<string>();

            for (int i = 0; i < folderItems.Count - 1; i++)
            {
                path.Add(GetFolderName(folderItems[i]));
            }

            return path;
        }

        private static string GetFolderName(FolderItemInfo folderItem)
        {
            return string.IsNullOrEmpty(folderItem.Name) ? EmptyValueName : folderItem.Name;
        }

        public List<SheetGroupNode> BuildSheetTree(Document doc)
        {
            List<ViewSheet> sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .OrderBy(s => s.SheetNumber ?? string.Empty, NameComparer)
                .ToList();

            BrowserOrganization organization = null;

            try
            {
                organization = BrowserOrganization.GetCurrentBrowserOrganizationForSheets(doc);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read sheets browser organization: {ex.Message}");
            }

            Logger.Info($"BrowserGroupService: found {sheets.Count} sheets.");

            GroupNodeBuilder root = new GroupNodeBuilder(string.Empty);

            foreach (ViewSheet sheet in sheets)
            {
                List<string> path = GetSheetFolderPath(organization, sheet);
                GroupNodeBuilder current = root;

                foreach (string segment in path)
                {
                    GroupNodeBuilder next;
                    if (!current.Children.TryGetValue(segment, out next))
                    {
                        next = new GroupNodeBuilder(segment);
                        current.Children[segment] = next;
                    }
                    current = next;
                }

                current.ViewIds.Add(sheet.Id);
            }

            return ConvertToSheetNodes(root.Children, doc);
        }

        private List<string> GetSheetFolderPath(BrowserOrganization organization, ViewSheet sheet)
        {
            if (organization == null)
            {
                return new List<string> { EmptyValueName };
            }

            IList<FolderItemInfo> folderItems;

            try
            {
                folderItems = organization.GetFolderItems(sheet.Id);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not get folder path for sheet '{sheet.SheetNumber}': {ex.Message}");
                return new List<string> { EmptyValueName };
            }

            if (folderItems == null || folderItems.Count == 0)
            {
                return new List<string> { EmptyValueName };
            }

            List<string> path = new List<string>();

            foreach (FolderItemInfo folderItem in folderItems)
            {
                path.Add(GetFolderName(folderItem));
            }

            return path;
        }

        private List<SheetGroupNode> ConvertToSheetNodes(Dictionary<string, GroupNodeBuilder> builders, Document doc)
        {
            List<SheetGroupNode> nodes = new List<SheetGroupNode>();

            foreach (KeyValuePair<string, GroupNodeBuilder> pair in builders.OrderBy(p => p.Key, NameComparer))
            {
                GroupNodeBuilder builder = pair.Value;
                SheetGroupNode node = new SheetGroupNode(builder.Name);

                foreach (SheetGroupNode child in ConvertToSheetNodes(builder.Children, doc))
                {
                    child.Parent = node;
                    node.Children.Add(child);
                }

                foreach (ElementId sheetId in builder.ViewIds)
                {
                    ViewSheet sheet = doc.GetElement(sheetId) as ViewSheet;
                    if (sheet != null)
                    {
                        SheetGroupNode sheetNode = new SheetGroupNode(sheet) { Parent = node };
                        node.Children.Add(sheetNode);
                    }
                }

                nodes.Add(node);
            }

            return nodes;
        }

        public List<ScheduleGroupNode> BuildScheduleTree(Document doc)
        {
            List<ViewSchedule> schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => ScheduleService.IsAvailableSchedule(s))
                .OrderBy(s => s.Name, NameComparer)
                .ToList();

            BrowserOrganization organization = null;

            try
            {
                organization = BrowserOrganization.GetCurrentBrowserOrganizationForViews(doc);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read schedules browser organization: {ex.Message}");
            }

            Logger.Info($"BrowserGroupService: found {schedules.Count} schedules.");

            GroupNodeBuilder root = new GroupNodeBuilder(string.Empty);

            foreach (ViewSchedule schedule in schedules)
            {
                List<string> path = GetFolderPath(organization, schedule);
                path = path
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s != UnknownFolderName)
                    .ToList();

                if (path.Count == 0)
                {
                    path = new List<string> { EmptyValueName };
                }

                GroupNodeBuilder current = root;

                foreach (string segment in path)
                {
                    GroupNodeBuilder next;
                    if (!current.Children.TryGetValue(segment, out next))
                    {
                        next = new GroupNodeBuilder(segment);
                        current.Children[segment] = next;
                    }
                    current = next;
                }

                current.ViewIds.Add(schedule.Id);
            }

            return ConvertToScheduleNodes(root.Children, doc);
        }

        private List<ScheduleGroupNode> ConvertToScheduleNodes(Dictionary<string, GroupNodeBuilder> builders, Document doc)
        {
            List<ScheduleGroupNode> nodes = new List<ScheduleGroupNode>();

            foreach (KeyValuePair<string, GroupNodeBuilder> pair in builders.OrderBy(p => p.Key, NameComparer))
            {
                GroupNodeBuilder builder = pair.Value;
                ScheduleGroupNode node = new ScheduleGroupNode(builder.Name);

                foreach (ScheduleGroupNode child in ConvertToScheduleNodes(builder.Children, doc))
                {
                    child.Parent = node;
                    node.Children.Add(child);
                }

                foreach (ElementId scheduleId in builder.ViewIds)
                {
                    ViewSchedule schedule = doc.GetElement(scheduleId) as ViewSchedule;
                    if (schedule != null)
                    {
                        ScheduleGroupNode scheduleNode = new ScheduleGroupNode(schedule) { Parent = node };
                        node.Children.Add(scheduleNode);
                    }
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private List<ViewGroupNode> ConvertToNodes(Dictionary<string, GroupNodeBuilder> builders)
        {
            List<ViewGroupNode> nodes = new List<ViewGroupNode>();

            foreach (KeyValuePair<string, GroupNodeBuilder> pair in builders.OrderBy(p => p.Key, NameComparer))
            {
                GroupNodeBuilder builder = pair.Value;
                ViewGroupNode node = new ViewGroupNode(builder.Name, new List<ElementId>(builder.ViewIds));

                foreach (ViewGroupNode child in ConvertToNodes(builder.Children))
                {
                    node.Children.Add(child);
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private class GroupNodeBuilder
        {
            public string Name { get; }
            public Dictionary<string, GroupNodeBuilder> Children { get; }
            public List<ElementId> ViewIds { get; }

            public GroupNodeBuilder(string name)
            {
                Name = name;
                Children = new Dictionary<string, GroupNodeBuilder>(StringComparer.Ordinal);
                ViewIds = new List<ElementId>();
            }
        }
    }
}
