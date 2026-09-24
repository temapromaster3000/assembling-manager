using System;
using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class SortamentResult
    {
        public int OriginalSegmentId { get; set; }
        public int FinalSegmentId { get; set; }
        public string OriginalSegmentName { get; set; }
        public string FinalSegmentName { get; set; }
        public int UpdatedSizeCount { get; set; }
        public int AddedSizeCount { get; set; }
        public int RemovedSizeCount { get; set; }
        public int MigratedPipeCount { get; set; }
        public int UpdatedRoutingPreferenceCount { get; set; }
        public bool UsedReplacement { get; set; }
        public bool OldSegmentDeleted { get; set; }
        public int ProcessedSegmentCount { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Details { get; } = new List<string>();
    }
}
