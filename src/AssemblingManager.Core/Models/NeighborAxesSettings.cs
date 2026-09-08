namespace AssemblingManager.Core.Models
{
    public class NeighborAxesSettings
    {
        public const double DefaultRadiusMm = 5.0;

        public double RadiusMm { get; set; } = DefaultRadiusMm;

        public string GridWorksetName { get; set; }
    }
}
