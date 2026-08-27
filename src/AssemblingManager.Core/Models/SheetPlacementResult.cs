using System;
using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class SheetPlacementResult
    {
        public int CreatedSheetsCount { get; set; }
        public int SignalSheetsCount { get; set; }
        public int FullyPlacedGroupsCount { get; set; }
        public List<string> Warnings { get; }
        public TimeSpan Elapsed { get; set; }

        public SheetPlacementResult()
        {
            Warnings = new List<string>();
        }
    }
}
