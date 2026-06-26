namespace AssemblingManager.Core.Models
{
    public class ViewConflictItem
    {
        public string AssemblyName { get; set; }
        public string ViewName { get; set; }
        public string ViewTypeDisplayName { get; set; }
        public bool Replace { get; set; }
    }
}
