using System;
using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class PipeAxisResult
    {
        public int ViewsProcessedCount { get; set; }
        public int LinesCreatedCount { get; set; }
        public int LinesDeletedCount { get; set; }
        public int PipesFoundCount { get; set; }
        public int PipesSkippedCount { get; set; }
        public int PipesFullyOccludedCount { get; set; }
        public int PipesPartiallyVisibleCount { get; set; }
        public int RaysCastCount { get; set; }

        public int HitsReceivedCount { get; set; }
        public TimeSpan Elapsed { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Details { get; } = new List<string>();
    }
}
