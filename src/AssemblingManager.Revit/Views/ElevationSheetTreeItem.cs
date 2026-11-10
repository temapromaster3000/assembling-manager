using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Revit.Services;

namespace AssemblingManager.Revit.Views
{
    /// <summary>
    /// Снимок дерева листов для немодального окна: хранит только имена и идентификаторы,
    /// чтобы долгоживущее окно не зависело от объектов Revit, которые могут быть удалены.
    /// </summary>
    public class ElevationSheetTreeItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public bool IsSheet { get; set; }
        public List<ElementId> SheetIds { get; set; }
        public List<ElevationSheetTreeItem> Children { get; set; }
        public ElevationSheetTreeItem Parent { get; set; }

        public static List<ElevationSheetTreeItem> Build(IEnumerable<SheetGroupNode> roots)
        {
            List<ElevationSheetTreeItem> items = new List<ElevationSheetTreeItem>();

            if (roots == null)
            {
                return items;
            }

            foreach (SheetGroupNode root in roots)
            {
                ElevationSheetTreeItem item = BuildNode(root);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static ElevationSheetTreeItem BuildNode(SheetGroupNode node)
        {
            if (node == null)
            {
                return null;
            }

            ElevationSheetTreeItem item = new ElevationSheetTreeItem
            {
                Name = node.IsSheet ? node.DisplayName : node.Name,
                DisplayName = node.DisplayName,
                IsSheet = node.IsSheet,
                SheetIds = node.GetAllSheets().Select(sheet => sheet.Id).ToList(),
                Children = new List<ElevationSheetTreeItem>()
            };

            foreach (SheetGroupNode child in node.Children)
            {
                ElevationSheetTreeItem childItem = BuildNode(child);
                if (childItem != null)
                {
                    childItem.Parent = item;
                    item.Children.Add(childItem);
                }
            }

            return item;
        }
    }
}
