using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class WorksharingSkippedElement
    {
        public ElementId Id { get; set; }
        public string Owner { get; set; }
        public string Reason { get; set; }
    }

    public class WorksharingEditability
    {
        public HashSet<ElementId> EditableIds { get; set; }
        public List<WorksharingSkippedElement> SkippedElements { get; set; }
    }

    public class WorksharingService
    {
        public WorksharingEditability CheckEditable(Document doc, ICollection<ElementId> elementIds)
        {
            WorksharingEditability result = new WorksharingEditability
            {
                EditableIds = new HashSet<ElementId>(),
                SkippedElements = new List<WorksharingSkippedElement>()
            };

            List<ElementId> candidates = new List<ElementId>();
            foreach (ElementId elementId in elementIds ?? (IEnumerable<ElementId>)new List<ElementId>())
            {
                if (elementId != null && doc.GetElement(elementId) != null && result.EditableIds.Add(elementId))
                {
                    candidates.Add(elementId);
                }
            }

            if (candidates.Count == 0 || !doc.IsWorkshared)
            {
                return result;
            }

            ICollection<ElementId> checkedOutIds;
            try
            {
                checkedOutIds = WorksharingUtils.CheckoutElements(doc, candidates);
            }
            catch (Exception ex)
            {
                Logger.Warn($"WorksharingUtils.CheckoutElements failed for {candidates.Count} elements: {ex.Message}. Proceeding without ownership check.");
                return result;
            }

            foreach (ElementId elementId in candidates)
            {
                if (checkedOutIds.Contains(elementId))
                {
                    continue;
                }

                result.EditableIds.Remove(elementId);
                result.SkippedElements.Add(new WorksharingSkippedElement
                {
                    Id = elementId,
                    Owner = GetOwner(doc, elementId),
                    Reason = GetReason(doc, elementId)
                });
            }

            return result;
        }

        public string GetOwnerName(Document doc, ElementId elementId)
        {
            return GetOwner(doc, elementId);
        }

        private static string GetOwner(Document doc, ElementId elementId)
        {
            try
            {
                WorksharingTooltipInfo info = WorksharingUtils.GetWorksharingTooltipInfo(doc, elementId);
                return string.IsNullOrWhiteSpace(info?.Owner) ? "неизвестно" : info.Owner;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Could not get owner for element {elementId}: {ex.Message}");
                return "неизвестно";
            }
        }

        private static string GetReason(Document doc, ElementId elementId)
        {
            try
            {
                ModelUpdatesStatus status = WorksharingUtils.GetModelUpdatesStatus(doc, elementId);
                if (status == ModelUpdatesStatus.DeletedInCentral)
                {
                    return "удалён в центральной модели";
                }

                if (status == ModelUpdatesStatus.UpdatedInCentral)
                {
                    return "изменён в центральной модели (требуется Reload Latest)";
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Could not get model updates status for element {elementId}: {ex.Message}");
            }

            return "занят другим пользователем";
        }
    }
}
