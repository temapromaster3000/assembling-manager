using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class ViewConflictResolution
    {
        public List<ViewConflictItem> Items { get; set; } = new List<ViewConflictItem>();
        public List<PlannedViewItem> SkipItems { get; set; } = new List<PlannedViewItem>();
    }
}
