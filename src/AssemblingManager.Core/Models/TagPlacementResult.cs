using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class TagPlacementResult
    {
        public int TagsCreatedCount { get; set; }
        public int TagsDeletedCount { get; set; }
        public int ElementsSkippedCount { get; set; }
        public int ElementsCutSkippedCount { get; set; }
        public int TaggedViewsCount { get; set; }
        public List<string> Warnings { get; }

        public TagPlacementResult()
        {
            Warnings = new List<string>();
        }
    }
}
