namespace AssemblingManager.Core.Models
{
    public class SheetConflictItem
    {
        public string ObjectName { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public int DuplicatesCount { get; set; }
        public bool Replace { get; set; }

        public string SheetDisplay
        {
            get { return $"{SheetNumber} — {SheetName}"; }
        }
    }
}
