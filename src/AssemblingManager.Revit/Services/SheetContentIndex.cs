using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class SheetContentIndex
    {
        private readonly Dictionary<ElementId, List<Viewport>> _viewportsBySheet;
        private readonly Dictionary<ElementId, List<ScheduleSheetInstance>> _schedulesBySheet;

        private SheetContentIndex(
            Dictionary<ElementId, List<Viewport>> viewportsBySheet,
            Dictionary<ElementId, List<ScheduleSheetInstance>> schedulesBySheet)
        {
            _viewportsBySheet = viewportsBySheet;
            _schedulesBySheet = schedulesBySheet;
        }

        public static SheetContentIndex Build(Document doc)
        {
            Dictionary<ElementId, List<Viewport>> viewportsBySheet = new Dictionary<ElementId, List<Viewport>>();

            foreach (Viewport viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (!viewportsBySheet.TryGetValue(viewport.OwnerViewId, out List<Viewport> list))
                {
                    list = new List<Viewport>();
                    viewportsBySheet.Add(viewport.OwnerViewId, list);
                }

                list.Add(viewport);
            }

            Dictionary<ElementId, List<ScheduleSheetInstance>> schedulesBySheet = new Dictionary<ElementId, List<ScheduleSheetInstance>>();

            foreach (ScheduleSheetInstance instance in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                if (!schedulesBySheet.TryGetValue(instance.OwnerViewId, out List<ScheduleSheetInstance> list))
                {
                    list = new List<ScheduleSheetInstance>();
                    schedulesBySheet.Add(instance.OwnerViewId, list);
                }

                list.Add(instance);
            }

            return new SheetContentIndex(viewportsBySheet, schedulesBySheet);
        }

        public List<Viewport> GetViewports(ElementId sheetId)
        {
            return _viewportsBySheet.TryGetValue(sheetId, out List<Viewport> viewports)
                ? viewports
                : new List<Viewport>();
        }

        public List<ScheduleSheetInstance> GetSchedules(ElementId sheetId)
        {
            return _schedulesBySheet.TryGetValue(sheetId, out List<ScheduleSheetInstance> instances)
                ? instances
                : new List<ScheduleSheetInstance>();
        }
    }
}
