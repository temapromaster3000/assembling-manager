namespace AssemblingManager.Core.Models
{
    public class PipeAxisSettings
    {
        public const bool DefaultSkipOccludedSegments = true;

        public string LineStyleName { get; set; }

        public bool SkipOccludedSegments { get; set; } = DefaultSkipOccludedSegments;
    }
}
