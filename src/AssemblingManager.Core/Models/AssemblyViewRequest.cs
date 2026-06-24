namespace AssemblingManager.Core.Models
{
    public class AssemblyViewRequest
    {
        public string AssemblyName { get; set; }
        public bool CreatePlan { get; set; }
        public bool CreateSection { get; set; }
        public bool Create3D { get; set; }
        public bool CreateSchedule { get; set; }
    }
}
