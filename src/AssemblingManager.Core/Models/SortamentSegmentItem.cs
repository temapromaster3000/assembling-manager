using System;

namespace AssemblingManager.Core.Models
{
    public class SortamentSegmentItem
    {
        public int SegmentId { get; set; }
        public string Name { get; set; }
        public string MaterialName { get; set; }
        public string ScheduleName { get; set; }
        public int UsedPipeCount { get; set; }

        public string DisplayName => UsedPipeCount == 0
            ? Name
            : $"{Name} ({UsedPipeCount} труб)";

        public string NameWithoutMaterialPrefix
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(MaterialName))
                {
                    return Name;
                }

                string materialPrefix = MaterialName.Trim() + " - ";
                return Name.StartsWith(materialPrefix, StringComparison.OrdinalIgnoreCase)
                    ? Name.Substring(materialPrefix.Length).Trim()
                    : Name;
            }
        }
    }
}
