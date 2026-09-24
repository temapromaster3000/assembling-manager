using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class SortamentService
    {
        private const double LengthTolerance = 0.0000001;
        private readonly string _operationId;

        public SortamentService()
        {
            _operationId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                           "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private void LogInfo(string message)
        {
            try
            {
                Logger.Info("[Sortaments:" + _operationId + "] " + message);
            }
            catch
            {
            }
        }

        private void LogDebug(string message)
        {
            try
            {
                Logger.Debug("[Sortaments:" + _operationId + "] " + message);
            }
            catch
            {
            }
        }

        private void LogWarn(string message)
        {
            try
            {
                Logger.Warn("[Sortaments:" + _operationId + "] " + message);
            }
            catch
            {
            }
        }

        private void LogError(string message)
        {
            try
            {
                Logger.Error("[Sortaments:" + _operationId + "] " + message);
            }
            catch
            {
            }
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string GetIdText(ElementId id)
        {
            if (id == null)
            {
                return "null";
            }

#pragma warning disable CS0618
            return id.IntegerValue.ToString(CultureInfo.InvariantCulture);
#pragma warning restore CS0618
        }

        private void LogSizes(
            string phase,
            ElementId segmentId,
            string segmentName,
            List<SizeSnapshot> sizes,
            ForgeTypeId unitId)
        {
            string unitName;
            try
            {
                unitName = UnitUtils.GetTypeCatalogStringForUnit(unitId);
            }
            catch
            {
                unitName = "unknown";
            }

            LogInfo(
                phase + " sizes segmentId=" + GetIdText(segmentId) +
                " name='" + segmentName + "' count=" + sizes.Count + " unit=" + unitName);

            foreach (SizeSnapshot size in sizes)
            {
                LogDebug(
                    phase + " size DN=" + FormatNumber(size.NominalDiameter) +
                    " OD=" + FormatNumber(size.OuterDiameter) +
                    " ID=" + FormatNumber(size.InnerDiameter) +
                    " WT=" + FormatNumber(size.WallThickness) +
                    " flags=" + size.UsedInSizeLists + "/" + size.UsedInSizing);
            }
        }

        private void LogPipeUsages(List<PipeUsage> pipeUsages, ForgeTypeId unitId)
        {
            LogInfo("pipe usages count=" + pipeUsages.Count);
            foreach (IGrouping<double, PipeUsage> group in pipeUsages.GroupBy(usage => usage.NominalDiameter))
            {
                double displayNominalDiameter = UnitUtils.ConvertFromInternalUnits(group.Key, unitId);
                LogInfo(
                    "pipe usage DN=" + FormatNumber(group.Key) +
                    " displayDN=" + FormatNumber(displayNominalDiameter) +
                    " count=" + group.Count());
            }
        }

        public ForgeTypeId GetPipeSizeUnitId(Document doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            FormatOptions formatOptions = doc.GetUnits().GetFormatOptions(SpecTypeId.PipeSize);
            return formatOptions.GetUnitTypeId();
        }

        public string GetUnitName(ForgeTypeId unitId)
        {
            return UnitUtils.GetTypeCatalogStringForUnit(unitId);
        }

        public List<SortamentSegmentItem> GetSegments(Document doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            Dictionary<int, int> usageCounts = GetPipeUsageCounts(doc);
            List<SortamentSegmentItem> result = new List<SortamentSegmentItem>();

            foreach (PipeSegment segment in new FilteredElementCollector(doc)
                         .OfClass(typeof(PipeSegment))
                         .Cast<PipeSegment>())
            {
                int segmentId = GetIdValue(segment.Id);
                ElementId materialId = segment.MaterialId;
                Material material = doc.GetElement(materialId) as Material;
                PipeScheduleType schedule = doc.GetElement(segment.ScheduleTypeId) as PipeScheduleType;
                int usedPipeCount = usageCounts.TryGetValue(segmentId, out int count) ? count : 0;

                LogDebug(
                    "segment id=" + segmentId +
                    " name='" + segment.Name + "'" +
                    " materialId=" + GetIdText(materialId) +
                    " materialElement=" + (material == null ? "null" : "Material") +
                    " material='" + (material?.Name ?? "null") + "'" +
                    " schedule='" + (schedule?.Name ?? "null") + "'" +
                    " pipes=" + usedPipeCount);

                result.Add(new SortamentSegmentItem
                {
                    SegmentId = segmentId,
                    Name = segment.Name,
                    MaterialName = material?.Name,
                    ScheduleName = schedule?.Name,
                    UsedPipeCount = usedPipeCount
                });
            }

            List<SortamentSegmentItem> sorted = result
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            LogInfo("segments discovered count=" + sorted.Count);
            foreach (SortamentSegmentItem item in sorted)
            {
                LogDebug(
                    "segment id=" + item.SegmentId +
                    " name='" + item.Name + "'" +
                    " material='" + (item.MaterialName ?? "null") + "'" +
                    " schedule='" + (item.ScheduleName ?? "null") + "'" +
                    " pipes=" + item.UsedPipeCount);
            }

            return sorted;
        }

        public List<SortamentSizeRow> GetSizeRows(Document doc, int segmentId, ForgeTypeId unitId)
        {
            PipeSegment segment = GetSegment(doc, segmentId);
            List<SizeSnapshot> snapshots = GetSizeSnapshots(segment);
            List<PipeUsage> pipeUsages = GetPipeUsages(doc, segment.Id);
            LogSizes("dialog-original", segment.Id, segment.Name, snapshots, unitId);
            List<SortamentSizeRow> rows = new List<SortamentSizeRow>();

            foreach (SizeSnapshot snapshot in snapshots)
            {
                rows.Add(new SortamentSizeRow
                {
                    IsExisting = true,
                    IsUsedByPipes = pipeUsages.Any(usage => IsNearlyEqual(usage.NominalDiameter, snapshot.NominalDiameter)),
                    NominalDiameter = UnitUtils.ConvertFromInternalUnits(snapshot.NominalDiameter, unitId),
                    OuterDiameter = UnitUtils.ConvertFromInternalUnits(snapshot.OuterDiameter, unitId),
                    WallThickness = UnitUtils.ConvertFromInternalUnits(
                        (snapshot.OuterDiameter - snapshot.InnerDiameter) / 2.0,
                        unitId),
                    UsedInSizeLists = snapshot.UsedInSizeLists,
                    UsedInSizing = snapshot.UsedInSizing
                });
            }

            List<SortamentSizeRow> sortedRows = rows
                .OrderBy(row => row.NominalDiameter)
                .ToList();
            LogInfo("dialog rows segmentId=" + segmentId + " count=" + sortedRows.Count);
            return sortedRows;
        }

        public SortamentResult Apply(
            Document doc,
            int segmentId,
            IEnumerable<SortamentSizeRow> rows,
            ForgeTypeId unitId,
            SortamentResult result)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            PipeSegment originalSegment = GetSegment(doc, segmentId);
            string originalSegmentName = originalSegment.Name;
            List<SizeSnapshot> originalSizes = GetSizeSnapshots(originalSegment);
            List<SizeSnapshot> requestedSizes = BuildRequestedSizes(rows, unitId);
            List<PipeUsage> pipeUsages = GetPipeUsages(doc, originalSegment.Id);
            LogInfo(
                "apply begin segmentId=" + segmentId +
                " name='" + originalSegmentName + "'" +
                " originalRows=" + originalSizes.Count +
                " requestedRows=" + requestedSizes.Count +
                " pipeCount=" + pipeUsages.Count);
            LogSizes("apply-original", originalSegment.Id, originalSegmentName, originalSizes, unitId);
            LogSizes("apply-requested", originalSegment.Id, originalSegmentName, requestedSizes, unitId);
            LogPipeUsages(pipeUsages, unitId);

            foreach (PipeUsage usage in pipeUsages)
            {
                if (FindSize(originalSizes, usage.NominalDiameter) == null)
                {
                    throw new InvalidOperationException("Номинальный диаметр трубы не найден в исходном каталоге.");
                }
            }

            List<double> usedNominalDiameters = pipeUsages.Select(usage => usage.NominalDiameter).ToList();

            ValidateRequestedSizes(originalSizes, requestedSizes, usedNominalDiameters);

            bool hasChanges = HasChanges(originalSizes, requestedSizes);
            if (!hasChanges)
            {
                LogInfo("apply strategy=noop");
                result.OriginalSegmentId = segmentId;
                result.FinalSegmentId = segmentId;
                result.OriginalSegmentName = originalSegmentName;
                result.FinalSegmentName = originalSegmentName;
                result.Details.Add("Изменений не обнаружено.");
                return result;
            }

            bool requiresReplacement = RequiresReplacement(originalSizes, requestedSizes);
            if (!requiresReplacement)
            {
                throw new InvalidOperationException("Изменения каталога не требуют замены сегмента.");
            }

            LogInfo("apply strategy=replacement");
            ApplyReplacement(doc, originalSegment, requestedSizes, pipeUsages, unitId, result);

            result.OriginalSegmentId = segmentId;
            result.OriginalSegmentName = originalSegmentName;
            return result;
        }

        private void ApplyReplacement(
            Document doc,
            PipeSegment originalSegment,
            List<SizeSnapshot> requestedSizes,
            List<PipeUsage> pipeUsages,
            ForgeTypeId unitId,
            SortamentResult result)
        {
            string originalSegmentName = originalSegment.Name;
            List<SizeSnapshot> originalSizes = GetSizeSnapshots(originalSegment);
            List<SizeSnapshot> removed = originalSizes
                .Where(original => FindSize(requestedSizes, original.NominalDiameter) == null)
                .ToList();
            List<SizeSnapshot> changed = originalSizes
                .Where(original =>
                {
                    SizeSnapshot requested = FindSize(requestedSizes, original.NominalDiameter);
                    return requested != null && !IsSameSize(original, requested);
                })
                .ToList();
            List<SizeSnapshot> added = requestedSizes
                .Where(requested => FindSize(originalSizes, requested.NominalDiameter) == null)
                .ToList();

            result.UpdatedSizeCount = changed.Count;
            result.AddedSizeCount = added.Count;
            result.RemovedSizeCount = removed.Count;

            LogInfo(
                "replacement source segmentId=" + GetIdText(originalSegment.Id) +
                " name='" + originalSegmentName + "'" +
                " materialId=" + GetIdText(originalSegment.MaterialId) +
                " scheduleId=" + GetIdText(originalSegment.ScheduleTypeId));
            LogSizes("replacement-requested", originalSegment.Id, originalSegmentName, requestedSizes, unitId);

            PipeSegment newSegment = CopyAndUpdateSegment(
                doc,
                originalSegment,
                originalSizes,
                requestedSizes,
                unitId,
                out bool usedCopy);

            string temporaryName = GetNextNumberedName(
                doc,
                typeof(PipeSegment),
                GetSegmentBaseName(doc, originalSegmentName));
            try
            {
                newSegment.Name = temporaryName;
            }
            catch (Exception ex)
            {
                temporaryName = newSegment.Name;
                result.Warnings.Add("Не удалось задать временное имя PipeSegment: " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(temporaryName))
            {
                throw new InvalidOperationException("Новый PipeSegment не получил допустимое имя.");
            }

            LogInfo("replacement temporaryName='" + temporaryName + "'");
            result.UpdatedRoutingPreferenceCount = ReplaceRoutingPreferences(
                doc,
                originalSegment.Id,
                newSegment.Id,
                result);

            MigratePipes(doc, originalSegment.Id, newSegment.Id, pipeUsages, requestedSizes, result);
            VerifySegmentCatalog(newSegment, requestedSizes, unitId, "replacement-after-migration");

            DeleteSegment(doc, originalSegment, result);

            string finalName = originalSegmentName;
            if (!TrySetName(newSegment, finalName))
            {
                finalName = newSegment.Name;
                result.Details.Add("Исходное имя недоступно для PipeSegment, оставлено имя " + finalName + ".");
            }

            LogInfo(
                "replacement final newSegmentId=" + GetIdText(newSegment.Id) +
                " name='" + finalName + "'" +
                " materialId=" + GetIdText(newSegment.MaterialId) +
                " scheduleId=" + GetIdText(newSegment.ScheduleTypeId) +
                " migratedPipes=" + result.MigratedPipeCount +
                " routingRules=" + result.UpdatedRoutingPreferenceCount);
            result.FinalSegmentId = GetIdValue(newSegment.Id);
            result.FinalSegmentName = finalName;
            result.UsedReplacement = true;
            result.OldSegmentDeleted = true;
            result.Details.Add(usedCopy
                ? "Создана копия PipeSegment, каталог обновлён и трубы перенесены."
                : "Создан заменяющий PipeSegment с исходным материалом, трубы перенесены.");
            result.Details.Add("Исходный материал сохранён.");
        }

        private PipeSegment CopyAndUpdateSegment(
            Document doc,
            PipeSegment originalSegment,
            List<SizeSnapshot> originalSizes,
            List<SizeSnapshot> requestedSizes,
            ForgeTypeId unitId,
            out bool usedCopy)
        {
            usedCopy = false;
            bool hasMaterial = IsUsableElementId(originalSegment.MaterialId);
            LogInfo(
                "copy attempt sourceSegmentId=" + GetIdText(originalSegment.Id) +
                " materialId=" + GetIdText(originalSegment.MaterialId) +
                " scheduleId=" + GetIdText(originalSegment.ScheduleTypeId) +
                " hasMaterial=" + hasMaterial);

            PipeSegment copiedSegment = TryCopySegmentWithDestinationTypes(
                doc,
                originalSegment,
                originalSizes,
                unitId);
            if (copiedSegment != null)
            {
                usedCopy = true;
                VerifyCopiedSegmentState(doc, originalSegment, copiedSegment, originalSizes, unitId);
                ApplyCatalogChanges(doc, copiedSegment, originalSizes, requestedSizes, unitId);
                return copiedSegment;
            }

            if (!hasMaterial)
            {
                throw new InvalidOperationException(
                    "Revit не смог скопировать PipeSegment без материала без изменения материала.");
            }

            LogInfo(
                "copy fallback create sourceSegmentId=" + GetIdText(originalSegment.Id) +
                " originalMaterialId=" + GetIdText(originalSegment.MaterialId));
            usedCopy = false;
            return CreateSegmentWithOriginalMaterial(doc, originalSegment, requestedSizes, unitId);
        }

        private PipeSegment TryCopySegmentWithDestinationTypes(
            Document doc,
            PipeSegment originalSegment,
            List<SizeSnapshot> originalSizes,
            ForgeTypeId unitId)
        {
            SubTransaction subTransaction = new SubTransaction(doc);
            try
            {
                subTransaction.Start();
                using (CopyPasteOptions copyOptions = new CopyPasteOptions())
                {
                    copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                        doc,
                        new List<ElementId> { originalSegment.Id },
                        doc,
                        Transform.Identity,
                        copyOptions);
                    LogInfo(
                        "copy returned ids=" +
                        string.Join(",", copiedIds.Select(GetIdText).ToArray()));

                    List<PipeSegment> copiedSegments = copiedIds
                        .Where(id => id != originalSegment.Id)
                        .Select(id => doc.GetElement(id) as PipeSegment)
                        .Where(segment => segment != null && segment.IsValidObject)
                        .ToList();
                    if (copiedSegments.Count != 1)
                    {
                        LogWarn(
                            "copy returned PipeSegment count=" + copiedSegments.Count +
                            "; rolling back copy attempt");
                        subTransaction.RollBack();
                        return null;
                    }

                    PipeSegment copiedSegment = copiedSegments[0];
                    doc.Regenerate();
                    LogInfo(
                        "copy created newSegmentId=" + GetIdText(copiedSegment.Id) +
                        " materialId=" + GetIdText(copiedSegment.MaterialId) +
                        " scheduleId=" + GetIdText(copiedSegment.ScheduleTypeId));
                    if (!AreSameElementId(originalSegment.MaterialId, copiedSegment.MaterialId))
                    {
                        LogWarn(
                            "copy changed materialId; rolling back copy attempt sourceId=" +
                            GetIdText(originalSegment.MaterialId) +
                            " copyId=" + GetIdText(copiedSegment.MaterialId));
                        subTransaction.RollBack();
                        return null;
                    }

                    List<SizeSnapshot> copiedSizes = GetSizeSnapshots(copiedSegment);
                    int copiedMismatches = CountSizeMismatches(copiedSizes, originalSizes);
                    LogInfo("copy initial verification mismatches=" + copiedMismatches);
                    if (copiedMismatches > 0)
                    {
                        LogWarn("copy changed source catalog; rolling back copy attempt");
                        subTransaction.RollBack();
                        return null;
                    }

                    subTransaction.Commit();
                    LogInfo("copy attempt committed newSegmentId=" + GetIdText(copiedSegment.Id));
                    return copiedSegment;
                }
            }
            catch (Exception ex)
            {
                if (subTransaction.HasStarted())
                {
                    subTransaction.RollBack();
                }

                LogWarn("copy attempt failed: " + ex.Message);
                return null;
            }
        }

        private PipeSegment CreateSegmentWithOriginalMaterial(
            Document doc,
            PipeSegment originalSegment,
            List<SizeSnapshot> requestedSizes,
            ForgeTypeId unitId)
        {
            ElementId materialId = originalSegment.MaterialId;
            if (!IsUsableElementId(materialId))
            {
                throw new InvalidOperationException(
                    "Для создания замены PipeSegment не найден исходный MaterialId.");
            }

            PipeScheduleType originalSchedule = doc.GetElement(originalSegment.ScheduleTypeId) as PipeScheduleType;
            PipeScheduleType newSchedule = CreateReplacementSchedule(doc, originalSchedule);
            List<MEPSize> mepSizes = new List<MEPSize>();
            try
            {
                foreach (SizeSnapshot size in requestedSizes)
                {
                    mepSizes.Add(CreateMepSize(size));
                }

                PipeSegment newSegment = PipeSegment.Create(
                    doc,
                    materialId,
                    newSchedule.Id,
                    mepSizes);
                doc.Regenerate();
                newSegment.Description = originalSegment.Description;
                newSegment.Roughness = originalSegment.Roughness;
                LogInfo(
                    "copy fallback created newSegmentId=" + GetIdText(newSegment.Id) +
                    " materialId=" + GetIdText(newSegment.MaterialId) +
                    " scheduleId=" + GetIdText(newSegment.ScheduleTypeId));
                if (!AreSameElementId(materialId, newSegment.MaterialId))
                {
                    throw new InvalidOperationException("Материал нового PipeSegment отличается от исходного.");
                }

                if (!AreSameElementId(newSchedule.Id, newSegment.ScheduleTypeId))
                {
                    throw new InvalidOperationException("Спецификация/тип нового PipeSegment отличается от созданного.");
                }

                VerifySegmentCatalog(newSegment, requestedSizes, unitId, "fallback-created");
                return newSegment;
            }
            finally
            {
                foreach (MEPSize size in mepSizes)
                {
                    size.Dispose();
                }
            }
        }

        private PipeScheduleType CreateReplacementSchedule(Document doc, PipeScheduleType originalSchedule)
        {
            string scheduleName = GetNextNumberedName(
                doc,
                typeof(PipeScheduleType),
                string.IsNullOrWhiteSpace(originalSchedule?.Name) ? "PipeSchedule" : originalSchedule.Name);

            if (originalSchedule != null)
            {
                PipeScheduleType duplicatedSchedule = originalSchedule.Duplicate(scheduleName) as PipeScheduleType;
                if (duplicatedSchedule == null)
                {
                    throw new InvalidOperationException("Не удалось создать копию Спецификации/Типа.");
                }

                return duplicatedSchedule;
            }

            PipeScheduleType createdSchedule = PipeScheduleType.Create(doc, scheduleName);
            if (createdSchedule == null)
            {
                throw new InvalidOperationException("Не удалось создать Спецификацию/Тип.");
            }

            return createdSchedule;
        }

        private void VerifyCopiedSegmentState(
            Document doc,
            PipeSegment originalSegment,
            PipeSegment copiedSegment,
            List<SizeSnapshot> originalSizes,
            ForgeTypeId unitId)
        {
            Material originalMaterial = doc.GetElement(originalSegment.MaterialId) as Material;
            Material copiedMaterial = doc.GetElement(copiedSegment.MaterialId) as Material;
            LogInfo(
                "copy material sourceId=" + GetIdText(originalSegment.MaterialId) +
                " sourceElement=" + (originalMaterial == null ? "null" : "Material") +
                " copyId=" + GetIdText(copiedSegment.MaterialId) +
                " copyElement=" + (copiedMaterial == null ? "null" : "Material"));
            LogInfo(
                "copy schedule sourceId=" + GetIdText(originalSegment.ScheduleTypeId) +
                " copyId=" + GetIdText(copiedSegment.ScheduleTypeId));

            if (!AreSameElementId(originalSegment.MaterialId, copiedSegment.MaterialId))
            {
                throw new InvalidOperationException("Копия PipeSegment изменила MaterialId.");
            }

            if (!AreSameElementId(originalSegment.ScheduleTypeId, copiedSegment.ScheduleTypeId))
            {
                LogInfo(
                    "copy changed scheduleId; using copied schedule sourceId=" +
                    GetIdText(originalSegment.ScheduleTypeId) +
                    " copyId=" + GetIdText(copiedSegment.ScheduleTypeId));
            }

            copiedSegment.Description = originalSegment.Description;
            copiedSegment.Roughness = originalSegment.Roughness;
            List<SizeSnapshot> copiedSizes = GetSizeSnapshots(copiedSegment);
            LogSizes("copy-before-update", copiedSegment.Id, copiedSegment.Name, copiedSizes, unitId);
            int copiedMismatches = CountSizeMismatches(copiedSizes, originalSizes);
            LogInfo("copy initial verification mismatches=" + copiedMismatches);
            if (copiedMismatches > 0)
            {
                throw new InvalidOperationException("Копия PipeSegment содержит другой исходный каталог.");
            }
        }

        private void ApplyCatalogChanges(
            Document doc,
            PipeSegment segment,
            List<SizeSnapshot> originalSizes,
            List<SizeSnapshot> requestedSizes,
            ForgeTypeId unitId)
        {
            List<SizeSnapshot> sizesToRemove = originalSizes
                .Where(original =>
                {
                    SizeSnapshot requested = FindSize(requestedSizes, original.NominalDiameter);
                    return requested == null || !IsSameSize(original, requested);
                })
                .ToList();
            List<SizeSnapshot> sizesToAdd = requestedSizes
                .Where(requested =>
                {
                    SizeSnapshot original = FindSize(originalSizes, requested.NominalDiameter);
                    return original == null || !IsSameSize(original, requested);
                })
                .ToList();

            SizeSnapshot temporarySize = null;
            if (sizesToRemove.Count > 0 && sizesToRemove.Count >= segment.SizeCount)
            {
                temporarySize = CreateTemporarySize(originalSizes, requestedSizes);
                LogInfo(
                    "copy temporary size begin DN=" + FormatNumber(temporarySize.NominalDiameter));
                using (MEPSize mepSize = CreateMepSize(temporarySize))
                {
                    segment.AddSize(mepSize);
                }

                doc.Regenerate();
                LogInfo("copy temporary size added");
            }

            foreach (SizeSnapshot size in sizesToRemove)
            {
                LogInfo(
                    "copy remove begin segmentId=" + GetIdText(segment.Id) +
                    " DN=" + FormatNumber(size.NominalDiameter));
                segment.RemoveSize(size.NominalDiameter);
                LogInfo(
                    "copy remove completed segmentId=" + GetIdText(segment.Id) +
                    " DN=" + FormatNumber(size.NominalDiameter));
            }

            if (sizesToRemove.Count > 0)
            {
                doc.Regenerate();
                foreach (SizeSnapshot size in sizesToRemove)
                {
                    if (FindSize(GetSizeSnapshots(segment), size.NominalDiameter) != null)
                    {
                        throw new InvalidOperationException(
                            "Не удалось удалить размер из копии PipeSegment: DN=" +
                            FormatNumber(size.NominalDiameter) + ".");
                    }
                }
            }

            foreach (SizeSnapshot size in sizesToAdd)
            {
                LogInfo(
                    "copy add begin segmentId=" + GetIdText(segment.Id) +
                    " DN=" + FormatNumber(size.NominalDiameter) +
                    " OD=" + FormatNumber(size.OuterDiameter) +
                    " ID=" + FormatNumber(size.InnerDiameter) +
                    " WT=" + FormatNumber(size.WallThickness));
                using (MEPSize mepSize = CreateMepSize(size))
                {
                    segment.AddSize(mepSize);
                }

                LogInfo(
                    "copy add completed segmentId=" + GetIdText(segment.Id) +
                    " DN=" + FormatNumber(size.NominalDiameter));
            }

            if (sizesToAdd.Count > 0)
            {
                doc.Regenerate();
            }

            if (temporarySize != null)
            {
                segment.RemoveSize(temporarySize.NominalDiameter);
                doc.Regenerate();
                LogInfo(
                    "copy temporary size removed DN=" +
                    FormatNumber(temporarySize.NominalDiameter));
            }

            VerifySegmentCatalog(segment, requestedSizes, unitId, "copy-after-update");
        }

        private SizeSnapshot CreateTemporarySize(
            List<SizeSnapshot> originalSizes,
            List<SizeSnapshot> requestedSizes)
        {
            double nominalDiameter = originalSizes
                .Concat(requestedSizes)
                .Select(size => size.NominalDiameter)
                .DefaultIfEmpty(1.0)
                .Max() + 1.0;
            while (FindSize(originalSizes, nominalDiameter) != null ||
                   FindSize(requestedSizes, nominalDiameter) != null)
            {
                nominalDiameter += 1.0;
            }

            return new SizeSnapshot
            {
                NominalDiameter = nominalDiameter,
                InnerDiameter = nominalDiameter * 0.9,
                OuterDiameter = nominalDiameter * 1.1,
                WallThickness = nominalDiameter * 0.1,
                UsedInSizeLists = true,
                UsedInSizing = true
            };
        }

        private void VerifySegmentCatalog(
            PipeSegment segment,
            List<SizeSnapshot> expectedSizes,
            ForgeTypeId unitId,
            string phase)
        {
            List<SizeSnapshot> actualSizes = GetSizeSnapshots(segment);
            LogSizes(phase, segment.Id, segment.Name, actualSizes, unitId);
            int mismatches = CountSizeMismatches(actualSizes, expectedSizes);
            LogInfo(phase + " verification mismatches=" + mismatches);
            if (mismatches > 0)
            {
                throw new InvalidOperationException(
                    "Проверка каталога не пройдена на этапе " + phase + ".");
            }
        }

        private void MigratePipes(
            Document doc,
            ElementId originalSegmentId,
            ElementId newSegmentId,
            List<PipeUsage> pipeUsages,
            List<SizeSnapshot> requestedSizes,
            SortamentResult result)
        {
            LogInfo(
                "migrate begin oldSegmentId=" + GetIdText(originalSegmentId) +
                " newSegmentId=" + GetIdText(newSegmentId) +
                " pipeCount=" + pipeUsages.Count);
            foreach (PipeUsage usage in pipeUsages)
            {
                Pipe pipe = doc.GetElement(usage.PipeId) as Pipe;
                if (pipe == null || !pipe.IsValidObject)
                {
                    throw new InvalidOperationException("Труба стала недействительной до замены сегмента.");
                }

                Parameter segmentParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SEGMENT_PARAM);
                if (segmentParameter == null || segmentParameter.IsReadOnly)
                {
                    throw new InvalidOperationException("Параметр Pipe Segment трубы недоступен для изменения.");
                }

                LogDebug(
                    "migrate segment begin pipeId=" + GetIdText(usage.PipeId) +
                    " oldSegmentId=" + GetIdText(originalSegmentId) +
                    " newSegmentId=" + GetIdText(newSegmentId) +
                    " DN=" + FormatNumber(usage.NominalDiameter));
                bool segmentSet = segmentParameter.Set(newSegmentId);
                LogDebug(
                    "migrate segment set pipeId=" + GetIdText(usage.PipeId) +
                    " returned=" + segmentSet +
                    " actual=" + GetIdText(segmentParameter.AsElementId()));
                if (!segmentSet && segmentParameter.AsElementId() != newSegmentId)
                {
                    throw new InvalidOperationException("Не удалось назначить новый Pipe Segment трубе.");
                }
            }

            doc.Regenerate();
            LogInfo("migrate regenerate after segment assignment");

            foreach (PipeUsage usage in pipeUsages)
            {
                Pipe pipe = doc.GetElement(usage.PipeId) as Pipe;
                if (pipe == null || !pipe.IsValidObject)
                {
                    throw new InvalidOperationException("Труба стала недействительной после назначения сегмента.");
                }

                SizeSnapshot requestedSize = FindSize(requestedSizes, usage.NominalDiameter);
                if (requestedSize == null)
                {
                    throw new InvalidOperationException("Для трубы не найден номинальный размер в новом сегменте.");
                }

                Parameter diameterParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diameterParameter == null || diameterParameter.IsReadOnly)
                {
                    throw new InvalidOperationException("Параметр Diameter трубы недоступен для изменения.");
                }

                LogDebug(
                    "migrate diameter begin pipeId=" + GetIdText(usage.PipeId) +
                    " requestedDN=" + FormatNumber(requestedSize.NominalDiameter));
                bool diameterSet = diameterParameter.Set(requestedSize.NominalDiameter);
                LogDebug(
                    "migrate diameter set pipeId=" + GetIdText(usage.PipeId) +
                    " returned=" + diameterSet +
                    " actual=" + FormatNumber(diameterParameter.AsDouble()));
                if (!diameterSet && !IsNearlyEqual(diameterParameter.AsDouble(), requestedSize.NominalDiameter))
                {
                    throw new InvalidOperationException("Не удалось назначить номинальный размер трубе.");
                }
            }

            doc.Regenerate();
            LogInfo("migrate regenerate after diameter assignment");

            foreach (PipeUsage usage in pipeUsages)
            {
                Pipe pipe = doc.GetElement(usage.PipeId) as Pipe;
                if (pipe == null || !pipe.IsValidObject)
                {
                    throw new InvalidOperationException("Проверка трубы после замены не выполнена.");
                }

                Parameter segmentParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SEGMENT_PARAM);
                Parameter diameterParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                ElementId assignedSegmentId = segmentParameter?.AsElementId();
                double assignedDiameter = diameterParameter?.AsDouble() ?? double.NaN;
                LogDebug(
                    "migrate verify pipeId=" + GetIdText(usage.PipeId) +
                    " expectedSegmentId=" + GetIdText(newSegmentId) +
                    " actualSegmentId=" + GetIdText(assignedSegmentId) +
                    " expectedDN=" + FormatNumber(usage.NominalDiameter) +
                    " actualDN=" + FormatNumber(assignedDiameter));

                if (assignedSegmentId == null || assignedSegmentId != newSegmentId ||
                    double.IsNaN(assignedDiameter) ||
                    !IsNearlyEqual(assignedDiameter, usage.NominalDiameter))
                {
                    throw new InvalidOperationException("Проверка результата переноса трубы не пройдена.");
                }

                result.MigratedPipeCount++;
            }

            LogInfo("migrate completed pipes=" + result.MigratedPipeCount);
        }

        private int ReplaceRoutingPreferences(
            Document doc,
            ElementId originalSegmentId,
            ElementId newSegmentId,
            SortamentResult result)
        {
            int replacedCount = 0;
            LogInfo(
                "routing replacement begin oldSegmentId=" + GetIdText(originalSegmentId) +
                " newSegmentId=" + GetIdText(newSegmentId));

            foreach (PipeType pipeType in new FilteredElementCollector(doc)
                         .OfClass(typeof(PipeType))
                         .Cast<PipeType>())
            {
                RoutingPreferenceManager manager = pipeType.RoutingPreferenceManager;
                if (manager == null || !manager.IsValidObject)
                {
                    continue;
                }

                List<int> indexes = new List<int>();
                int ruleCount = manager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                LogDebug(
                    "routing pipeType id=" + GetIdText(pipeType.Id) +
                    " name='" + pipeType.Name + "' rules=" + ruleCount);
                for (int i = 0; i < ruleCount; i++)
                {
                    RoutingPreferenceRule rule = manager.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                    if (rule != null && rule.MEPPartId == originalSegmentId)
                    {
                        indexes.Add(i);
                    }
                }

                for (int i = indexes.Count - 1; i >= 0; i--)
                {
                    int index = indexes[i];
                    RoutingPreferenceRule oldRule = manager.GetRule(RoutingPreferenceRuleGroupType.Segments, index);
                    if (oldRule == null || oldRule.MEPPartId != originalSegmentId)
                    {
                        continue;
                    }

                    RoutingPreferenceRule newRule = new RoutingPreferenceRule(newSegmentId, oldRule.Description);
                    for (int criterionIndex = 0; criterionIndex < oldRule.NumberOfCriteria; criterionIndex++)
                    {
                        RoutingCriterionBase criterion = oldRule.GetCriterion(criterionIndex);
                        if (criterion is PrimarySizeCriterion primarySizeCriterion)
                        {
                            newRule.AddCriterion(new PrimarySizeCriterion(
                                primarySizeCriterion.MinimumSize,
                                primarySizeCriterion.MaximumSize));
                        }
                        else if (criterion != null)
                        {
                            newRule.AddCriterion(criterion);
                        }
                    }

                    LogDebug(
                        "routing rule replace pipeType='" + pipeType.Name + "'" +
                        " index=" + index +
                        " oldMepPartId=" + GetIdText(oldRule.MEPPartId) +
                        " newMepPartId=" + GetIdText(newSegmentId) +
                        " criteria=" + oldRule.NumberOfCriteria);
                    manager.RemoveRule(RoutingPreferenceRuleGroupType.Segments, index);
                    manager.AddRule(RoutingPreferenceRuleGroupType.Segments, newRule, index);
                    replacedCount++;
                }
            }

            if (replacedCount == 0)
            {
                LogWarn("routing replacement matched 0 rules");
                result.Warnings.Add("Не найдено Routing Preferences, ссылающихся на исходный PipeSegment.");
            }
            else
            {
                LogInfo("routing replacement completed rules=" + replacedCount);
            }

            return replacedCount;
        }

        private void DeleteSegment(Document doc, PipeSegment segment, SortamentResult result)
        {
            ElementId segmentId = segment.Id;
            LogInfo("delete segment begin id=" + GetIdText(segmentId) + " name='" + segment.Name + "'");
            ICollection<ElementId> deletedIds = doc.Delete(segmentId);
            if (doc.GetElement(segmentId) != null)
            {
                throw new InvalidOperationException("Старый PipeSegment не удалился.");
            }

            LogInfo("delete segment completed id=" + GetIdText(segmentId) + " deletedCount=" + (deletedIds?.Count ?? 0));
            result.OldSegmentDeleted = true;
            result.Details.Add("Удалено элементов: " + (deletedIds?.Count ?? 0) + ".");
        }

        private void ValidateRequestedSizes(
            List<SizeSnapshot> originalSizes,
            List<SizeSnapshot> requestedSizes,
            List<double> usedNominalDiameters)
        {
            if (requestedSizes.Count == 0)
            {
                throw new InvalidOperationException("В сегменте должен остаться хотя бы один размер.");
            }

            foreach (SizeSnapshot size in requestedSizes)
            {
                if (!IsFinitePositive(size.NominalDiameter) ||
                    !IsFinitePositive(size.OuterDiameter) ||
                    !IsFinitePositive(size.WallThickness) ||
                    !IsFinitePositive(size.InnerDiameter))
                {
                    throw new InvalidOperationException("Размеры должны быть положительными числами.");
                }

                if (!IsNearlyEqual(
                        size.InnerDiameter,
                        size.OuterDiameter - 2.0 * size.WallThickness))
                {
                    throw new InvalidOperationException("Внутренний диаметр не соответствует толщине стенки.");
                }

                if (requestedSizes.Count(item => IsNearlyEqual(item.NominalDiameter, size.NominalDiameter)) > 1)
                {
                    throw new InvalidOperationException("Номинальные диаметры должны быть уникальными.");
                }
            }

            foreach (SizeSnapshot original in originalSizes)
            {
                if (FindSize(requestedSizes, original.NominalDiameter) != null)
                {
                    continue;
                }

                if (usedNominalDiameters.Any(value => IsNearlyEqual(value, original.NominalDiameter)))
                {
                    throw new InvalidOperationException("Нельзя удалить номинальный диаметр, используемый трубами в проекте.");
                }

                if (requestedSizes.Count == 0)
                {
                    throw new InvalidOperationException("Нельзя удалить последний размер сегмента.");
                }
            }
        }

        private bool RequiresReplacement(
            List<SizeSnapshot> originalSizes,
            List<SizeSnapshot> requestedSizes)
        {
            foreach (SizeSnapshot original in originalSizes)
            {
                SizeSnapshot requested = FindSize(requestedSizes, original.NominalDiameter);
                if (requested == null || !IsSameSize(original, requested))
                {
                    return true;
                }
            }

            foreach (SizeSnapshot requested in requestedSizes)
            {
                if (FindSize(originalSizes, requested.NominalDiameter) == null)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasChanges(List<SizeSnapshot> originalSizes, List<SizeSnapshot> requestedSizes)
        {
            if (originalSizes.Count != requestedSizes.Count)
            {
                return true;
            }

            foreach (SizeSnapshot original in originalSizes)
            {
                SizeSnapshot requested = FindSize(requestedSizes, original.NominalDiameter);
                if (requested == null || !IsSameSize(original, requested))
                {
                    return true;
                }
            }

            return false;
        }

        private List<SizeSnapshot> BuildRequestedSizes(IEnumerable<SortamentSizeRow> rows, ForgeTypeId unitId)
        {
            List<SizeSnapshot> result = new List<SizeSnapshot>();

            foreach (SortamentSizeRow row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                double nominalDiameter = UnitUtils.ConvertToInternalUnits(row.NominalDiameter, unitId);
                double outerDiameter = UnitUtils.ConvertToInternalUnits(row.OuterDiameter, unitId);
                double wallThickness = UnitUtils.ConvertToInternalUnits(row.WallThickness, unitId);
                double innerDiameter = outerDiameter - 2.0 * wallThickness;
                LogDebug(
                    "requested row inputDN=" + FormatNumber(row.NominalDiameter) +
                    " inputOD=" + FormatNumber(row.OuterDiameter) +
                    " inputWT=" + FormatNumber(row.WallThickness) +
                    " internalDN=" + FormatNumber(nominalDiameter) +
                    " internalOD=" + FormatNumber(outerDiameter) +
                    " internalID=" + FormatNumber(innerDiameter) +
                    " internalWT=" + FormatNumber(wallThickness));

                result.Add(new SizeSnapshot
                {
                    NominalDiameter = nominalDiameter,
                    OuterDiameter = outerDiameter,
                    InnerDiameter = innerDiameter,
                    WallThickness = wallThickness,
                    UsedInSizeLists = row.UsedInSizeLists,
                    UsedInSizing = row.UsedInSizing
                });
            }

            return result;
        }

        private List<SizeSnapshot> GetSizeSnapshots(PipeSegment segment)
        {
            List<SizeSnapshot> result = new List<SizeSnapshot>();
            foreach (MEPSize size in segment.GetSizes())
            {
                using (size)
                {
                    if (size == null || !size.IsValidObject)
                    {
                        continue;
                    }

                    result.Add(new SizeSnapshot
                    {
                        NominalDiameter = size.NominalDiameter,
                        OuterDiameter = size.OuterDiameter,
                        InnerDiameter = size.InnerDiameter,
                        WallThickness = (size.OuterDiameter - size.InnerDiameter) / 2.0,
                        UsedInSizeLists = size.UsedInSizeLists,
                        UsedInSizing = size.UsedInSizing
                    });
                }
            }

            return result;
        }

        private List<PipeUsage> GetPipeUsages(Document doc, ElementId segmentId)
        {
            List<PipeUsage> result = new List<PipeUsage>();

            foreach (Pipe pipe in new FilteredElementCollector(doc)
                         .OfClass(typeof(Pipe))
                         .Cast<Pipe>())
            {
                Parameter segmentParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SEGMENT_PARAM);
                if (segmentParameter == null || segmentParameter.AsElementId() != segmentId)
                {
                    continue;
                }

                Parameter diameterParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diameterParameter == null)
                {
                    throw new InvalidOperationException("У трубы отсутствует параметр Diameter.");
                }

                double nominalDiameter = diameterParameter.AsDouble();

                if (!IsFinitePositive(nominalDiameter))
                {
                    throw new InvalidOperationException("Не удалось прочитать номинальный диаметр трубы.");
                }

                result.Add(new PipeUsage
                {
                    PipeId = pipe.Id,
                    NominalDiameter = nominalDiameter
                });
            }

            return result;
        }

        private Dictionary<int, int> GetPipeUsageCounts(Document doc)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();

            foreach (Pipe pipe in new FilteredElementCollector(doc)
                         .OfClass(typeof(Pipe))
                         .Cast<Pipe>())
            {
                Parameter segmentParameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SEGMENT_PARAM);
                ElementId segmentId = segmentParameter?.AsElementId();
                if (segmentId == null)
                {
                    continue;
                }

                int id = GetIdValue(segmentId);
                result.TryGetValue(id, out int count);
                result[id] = count + 1;
            }

            return result;
        }

        private PipeSegment GetSegment(Document doc, int segmentId)
        {
#pragma warning disable CS0618
            PipeSegment segment = doc.GetElement(new ElementId(segmentId)) as PipeSegment;
#pragma warning restore CS0618
            if (segment == null || !segment.IsValidObject)
            {
                throw new InvalidOperationException("Выбранный PipeSegment не найден в документе.");
            }

            return segment;
        }

        private static bool IsUsableElementId(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId;
        }

        private static bool AreSameElementId(ElementId first, ElementId second)
        {
            if (first == null || second == null)
            {
                return first == null && second == null;
            }

            return first == second;
        }

        private static string GetSegmentBaseName(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "PipeSegment";
            }

            string trimmedName = name.Trim();
            int end = trimmedName.Length;
            while (end > 0 && char.IsDigit(trimmedName[end - 1]))
            {
                end--;
            }

            if (end == 0 || end == trimmedName.Length)
            {
                return trimmedName;
            }

            string baseName = trimmedName.Substring(0, end);
            int suffixLength = trimmedName.Length - end;
            bool hasNumberedRelative = new FilteredElementCollector(doc)
                .OfClass(typeof(PipeSegment))
                .Cast<Element>()
                .Where(element => !string.Equals(element.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
                .Any(element => HasNumericSuffix(element.Name, baseName));

            return suffixLength == 1 || hasNumberedRelative ? baseName : trimmedName;
        }

        private static bool HasNumericSuffix(string value, string baseName)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(baseName) ||
                value.Length <= baseName.Length ||
                !value.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = baseName.Length; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private string GetNextNumberedName(Document doc, Type elementType, string baseName)
        {
            HashSet<string> existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(elementType)
                    .Cast<Element>()
                    .Select(element => element.Name),
                StringComparer.OrdinalIgnoreCase);

            string safeBaseName = string.IsNullOrWhiteSpace(baseName) ? "PipeSegment" : baseName.Trim();
            for (int number = 1; number < int.MaxValue; number++)
            {
                string candidate = safeBaseName + number;
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Не удалось сгенерировать уникальное имя.");
        }

        private bool TrySetName(PipeSegment segment, string name)
        {
            try
            {
                segment.Name = name;
                return string.Equals(segment.Name, name, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static MEPSize CreateMepSize(SizeSnapshot size)
        {
            return new MEPSize(
                size.NominalDiameter,
                size.InnerDiameter,
                size.OuterDiameter,
                size.UsedInSizeLists,
                size.UsedInSizing);
        }

        private int CountSizeMismatches(List<SizeSnapshot> actualSizes, List<SizeSnapshot> expectedSizes)
        {
            int mismatches = 0;
            foreach (SizeSnapshot expected in expectedSizes)
            {
                SizeSnapshot actual = FindSize(actualSizes, expected.NominalDiameter);
                if (actual == null || !IsSameSize(expected, actual))
                {
                    mismatches++;
                }
            }

            foreach (SizeSnapshot actual in actualSizes)
            {
                if (FindSize(expectedSizes, actual.NominalDiameter) == null)
                {
                    mismatches++;
                }
            }

            return mismatches;
        }

        private static SizeSnapshot FindSize(IEnumerable<SizeSnapshot> sizes, double nominalDiameter)
        {
            return sizes.FirstOrDefault(size => IsNearlyEqual(size.NominalDiameter, nominalDiameter));
        }

        private static bool IsSameSize(SizeSnapshot first, SizeSnapshot second)
        {
            return IsNearlyEqual(first.NominalDiameter, second.NominalDiameter) &&
                   IsNearlyEqual(first.OuterDiameter, second.OuterDiameter) &&
                   IsNearlyEqual(first.InnerDiameter, second.InnerDiameter) &&
                   first.UsedInSizeLists == second.UsedInSizeLists &&
                   first.UsedInSizing == second.UsedInSizing;
        }

        private static bool IsNearlyEqual(double first, double second)
        {
            return Math.Abs(first - second) <= LengthTolerance;
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static int GetIdValue(ElementId id)
        {
#pragma warning disable CS0618
            return id.IntegerValue;
#pragma warning restore CS0618
        }

        private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        private sealed class SizeSnapshot
        {
            public double NominalDiameter { get; set; }
            public double OuterDiameter { get; set; }
            public double InnerDiameter { get; set; }
            public double WallThickness { get; set; }
            public bool UsedInSizeLists { get; set; }
            public bool UsedInSizing { get; set; }
        }

        private sealed class PipeUsage
        {
            public ElementId PipeId { get; set; }
            public double NominalDiameter { get; set; }
        }
    }
}
