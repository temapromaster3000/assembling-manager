using System.Collections.Generic;

namespace AssemblingManager.Revit.Services
{
    /// <summary>
    /// Результат предпросмотра изменения одного каталога PipeSegment.
    /// Содержит проверенную модель изменения каталога и справочные данные для пользователя.
    /// </summary>
    public sealed class SortamentChangePlan
    {
        public int SegmentId { get; set; }
        public string SegmentName { get; set; }
        public string CatalogHash { get; set; }
        public int UsedPipeCount { get; set; }
        public int RoutingPreferenceRuleCount { get; set; }
        public bool HasChanges { get; set; }
        public bool RequiresReplacement { get; set; }
        public int AddedSizeCount { get; set; }
        public int UpdatedSizeCount { get; set; }
        public int RemovedSizeCount { get; set; }
        public List<SortamentSizeChange> Changes { get; set; } = new List<SortamentSizeChange>();
        public List<SortamentSizeChange> ProposedSizes { get; set; } = new List<SortamentSizeChange>();
        public List<SortamentWorksharingBlocker> Blockers { get; set; } = new List<SortamentWorksharingBlocker>();
        public List<string> Warnings { get; set; } = new List<string>();

        internal List<SizeSnapshot> OriginalSizes { get; set; }
        internal List<SizeSnapshot> RequestedSizes { get; set; }
        internal List<PipeUsage> PipeUsages { get; set; }
    }

    /// <summary>
    /// Один размер каталога в единицах документа.
    /// Для update заполнены Previous* поля, для remove — удаляемый размер.
    /// </summary>
    public sealed class SortamentSizeChange
    {
        public string Operation { get; set; }
        public double NominalDiameter { get; set; }
        public double OuterDiameter { get; set; }
        public double WallThickness { get; set; }
        public double InnerDiameter { get; set; }
        public bool UsedInSizeLists { get; set; }
        public bool UsedInSizing { get; set; }
        public bool UsedByPipes { get; set; }
        public int UsedPipeCount { get; set; }
        public double? PreviousOuterDiameter { get; set; }
        public double? PreviousWallThickness { get; set; }
        public bool? PreviousUsedInSizeLists { get; set; }
        public bool? PreviousUsedInSizing { get; set; }
    }

    /// <summary>
    /// Элемент, который занят другим пользователем или устарел в центральной модели.
    /// </summary>
    public sealed class SortamentWorksharingBlocker
    {
        public int ElementId { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Owner { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class SizeSnapshot
    {
        public double NominalDiameter { get; set; }
        public double OuterDiameter { get; set; }
        public double InnerDiameter { get; set; }
        public double WallThickness { get; set; }
        public bool UsedInSizeLists { get; set; }
        public bool UsedInSizing { get; set; }
    }

    internal sealed class PipeUsage
    {
        public Autodesk.Revit.DB.ElementId PipeId { get; set; }
        public double NominalDiameter { get; set; }
    }
}
