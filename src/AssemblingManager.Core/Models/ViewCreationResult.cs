using System;

namespace AssemblingManager.Core.Models
{
    public class ViewCreationResult
    {
        public int CreatedCount { get; set; }
        public int ReplacedCount { get; set; }
        public int SkippedCount { get; set; }
        public TimeSpan Elapsed { get; set; }
    }
}
