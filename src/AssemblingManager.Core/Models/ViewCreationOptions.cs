namespace AssemblingManager.Core.Models
{
    public class ViewCreationOptions
    {
        public bool UseExistingGroupingParameter { get; set; }
        public bool CreateNewParameter { get; set; }
        public int MissingCategoriesCount { get; set; }

        public bool CreatePlan { get; set; }
        public bool CreateFrontView { get; set; }
        public bool CreateBackView { get; set; }
        public bool CreateRightView { get; set; }
        public bool CreateLeftView { get; set; }
        public bool Create3D { get; set; }
        public bool CreateSchedule { get; set; }

        public int? PlanTemplateId { get; set; }
        public int? SectionTemplateId { get; set; }
        public int? View3DTemplateId { get; set; }
        public int? MasterScheduleId { get; set; }
        public int? ScheduleViewTemplateId { get; set; }

        public int? PlanViewFamilyTypeId { get; set; }
        public int? SectionViewFamilyTypeId { get; set; }
        public int? View3DViewFamilyTypeId { get; set; }
    }
}
