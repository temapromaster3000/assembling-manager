using System;
using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class NeighborAxesResult
    {
        public int ViewsProcessedCount { get; set; }
        public int MarkersCreatedCount { get; set; }
        public int MarkersReplacedCount { get; set; }
        public int ViewsSkippedCount { get; set; }
        public TimeSpan Elapsed { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Details { get; } = new List<string>();
    }
}
