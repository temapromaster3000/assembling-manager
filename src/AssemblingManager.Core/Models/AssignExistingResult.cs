using System.Collections.Generic;

namespace AssemblingManager.Core.Models
{
    public class AssignExistingResult
    {
        public string ParameterName { get; set; }

        public string AssemblyName { get; set; }

        public int TotalElementCount { get; set; }

        public int NestedCount { get; set; }

        public int WrittenCount { get; set; }

        public int SkippedReadOnlyCount { get; set; }

        public int SkippedNoParameterCount { get; set; }

        public int SkippedDisallowedCount { get; set; }

        public List<string> SkippedDisallowedCategoryNames { get; set; }

        public List<string> AddedParameterCategories { get; set; }

        public AssignExistingResult()
        {
            SkippedDisallowedCategoryNames = new List<string>();
            AddedParameterCategories = new List<string>();
        }
    }
}
