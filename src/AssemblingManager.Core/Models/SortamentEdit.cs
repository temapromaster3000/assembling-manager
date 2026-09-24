using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class SortamentEdit
    {
        public int SegmentId { get; set; }
        public List<SortamentSizeRow> Rows { get; set; } = new List<SortamentSizeRow>();
    }
}
