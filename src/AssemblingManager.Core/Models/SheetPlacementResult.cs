using System;
using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class SheetPlacementResult
    {
        public int CreatedSheetsCount { get; set; }
        public int UpdatedSheetsCount { get; set; }
        public int SkippedObjectsCount { get; set; }
        public List<string> Warnings { get; }
        public TimeSpan Elapsed { get; set; }

        public SheetPlacementResult()
        {
            Warnings = new List<string>();
        }
    }
}
