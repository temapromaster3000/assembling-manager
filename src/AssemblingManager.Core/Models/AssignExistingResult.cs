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

        public List<string> AddedParameterCategories { get; set; }

        public List<string> AddedFilterCategories { get; set; }

        public bool FilterCreated { get; set; }

        public bool FilterRecreated { get; set; }

        public AssignExistingResult()
        {
            AddedParameterCategories = new List<string>();
            AddedFilterCategories = new List<string>();
        }
    }
}
