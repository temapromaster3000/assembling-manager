using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class TagService
    {
        private static readonly Guid TagSchemaGuid =
            new Guid("2E7A9C13-5B08-4D21-A965-8F43C1B0D6E7");

        public const string AssemblyNameField = "AssemblyName";
        public const string SkipOptionName = "— Пропустить —";
        private const string PositionParameterName = "ADSK_Позиция";

        private const double ElbowMargin = 1.0 / 304.8;
        private const double HeadMargin = 3.0 / 304.8;
        private const int MaxLadderSteps = 8;

        private struct TagDims
        {
            public double Width;
            public double Height;
            public double RightGap;
            public double LeftGap;
            public double UpGap;
            public double DownGap;
        }

        private readonly Dictionary<string, TagDims> _tagDimsCache =
            new Dictionary<string, TagDims>(StringComparer.Ordinal);

        private readonly ViewService _viewService = new ViewService();

        private static readonly HashSet<string> ExcludedTagCategoryNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "OST_KeynoteTags",
                "OST_BeamSystemTags",
                "OST_ElectricalCircuitTags",
                "OST_MaterialTags",
                "OST_RoomTags",
                "OST_SpaceTags",
                "OST_PanelTags",
                "OST_Grids",
                "OST_Levels"
            };

        private static readonly Dictionary<string, string[]> ElementToTagCategoryMap =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "OST_Walls", new[] { "OST_WallsTags" } },
                { "OST_Doors", new[] { "OST_DoorsTags" } },
                { "OST_Windows", new[] { "OST_WindowsTags" } },
                { "OST_Floors", new[] { "OST_FloorsTags" } },
                { "OST_Ceilings", new[] { "OST_CeilingsTags" } },
                { "OST_Roofs", new[] { "OST_RoofsTags" } },
                { "OST_Furniture", new[] { "OST_FurnitureTags" } },
                { "OST_Casework", new[] { "OST_CaseworkTags" } },
                { "OST_Stairs", new[] { "OST_StairsTags" } },
                { "OST_Ramps", new[] { "OST_RampsTags" } },
                { "OST_SpecialtyEquipment", new[] { "OST_SpecialtyEquipmentTags" } },
                { "OST_Parking", new[] { "OST_ParkingTags" } },
                { "OST_Site", new[] { "OST_SiteTags" } },
                { "OST_GenericModel", new[] { "OST_GenericModelTags", "OST_ModelTags" } },
                { "OST_StructuralColumns", new[] { "OST_StructColumnTags", "OST_StructuralColumnTags" } },
                { "OST_PipeCurves", new[] { "OST_PipeTags" } },
                { "OST_PipeFitting", new[] { "OST_PipeFittingTags" } },
                { "OST_PipeAccessory", new[] { "OST_PipeAccessoryTags" } },
                { "OST_DuctCurves", new[] { "OST_DuctTags" } },
                { "OST_DuctFitting", new[] { "OST_DuctFittingTags" } },
                { "OST_MechanicalEquipment", new[] { "OST_MechanicalEquipmentTags" } },
                { "OST_PlumbingFixtures", new[] { "OST_PlumbingFixturesTags" } },
                { "OST_LightingFixtures", new[] { "OST_LightingFixturesTags" } },
                { "OST_ElectricalFixtures", new[] { "OST_ElectricalFixturesTags" } },
                { "OST_Conduit", new[] { "OST_ConduitTags" } },
                { "OST_CableTray", new[] { "OST_CableTrayTags" } },
                { "OST_Sprinklers", new[] { "OST_SprinklersTags" } },
                { "OST_CommunicationDevices", new[] { "OST_CommunicationDevicesTags" } },
                { "OST_FireAlarmDevices", new[] { "OST_FireAlarmDevicesTags" } }
            };

        public class TagSymbolOption
        {
            public FamilySymbol Symbol { get; }
            public bool IsMultiCategory { get; }

            public TagSymbolOption(FamilySymbol symbol, bool isMultiCategory)
            {
                Symbol = symbol;
                IsMultiCategory = isMultiCategory;
            }

            public string DisplayName
            {
                get { return $"{Symbol.Family.Name} : {Symbol.Name}"; }
            }
        }

        private Schema GetTagSchema()
        {
            Schema schema = Schema.Lookup(TagSchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder builder = new SchemaBuilder(TagSchemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("AssemblingManagerPositionTag");
            builder.AddSimpleField(AssemblyNameField, typeof(string));
            return builder.Finish();
        }

        public List<Category> GetElementCategories(Document doc, IEnumerable<ViewSchedule> schedules)
        {
            Dictionary<int, Category> result = new Dictionary<int, Category>();

            foreach (ViewSchedule schedule in schedules)
            {
                if (schedule == null || !schedule.IsValidObject)
                {
                    continue;
                }

                foreach (Element element in CollectScheduleElements(doc, schedule))
                {
                    Category category = element.Category;
                    if (category == null)
                    {
                        continue;
                    }

#pragma warning disable CS0618
                    result[category.Id.IntegerValue] = category;
#pragma warning restore CS0618
                }
            }

            return result.Values
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<Element> CollectScheduleElements(Document doc, ViewSchedule schedule)
        {
            if (schedule == null || !schedule.IsValidObject)
            {
                return new List<Element>();
            }

            return new FilteredElementCollector(doc, schedule.Id)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => !(e is Material))
                .ToList();
        }

        public Dictionary<ElementId, List<TagSymbolOption>> BuildTagSymbolOptions(
            Document doc,
            IEnumerable<Category> categories)
        {
            List<Category> categoryList = categories?.ToList() ?? new List<Category>();

            Logger.Info(
                $"Tag option builder: {categoryList.Count} element categories; " +
                "project families scanned for tag symbols.");

            foreach (Category category in categoryList)
            {
                Logger.Info($"  Element category '{category.Name}' ({GetBuiltInCategoryName(category)}).");
            }

            Dictionary<string, List<FamilySymbol>> symbolsByTagCategoryName =
                new Dictionary<string, List<FamilySymbol>>(StringComparer.Ordinal);
            Dictionary<string, int> symbolCountByFamilyCategory =
                new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, string> familyCategoryDisplay =
                new Dictionary<string, string>(StringComparer.Ordinal);
            List<TagSymbolOption> multiCategoryOptions = new List<TagSymbolOption>();
            HashSet<ElementId> collectedSymbols = new HashSet<ElementId>();

            foreach (FamilySymbol symbol in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>())
            {
                Category category = GetSymbolCategory(symbol);
                if (category == null)
                {
                    continue;
                }

                string bicName = GetBuiltInCategoryName(category);
                if (string.IsNullOrEmpty(bicName))
                {
                    continue;
                }

                int existingCount;
                if (!symbolCountByFamilyCategory.TryGetValue(bicName, out existingCount))
                {
                    symbolCountByFamilyCategory[bicName] = 0;
                    familyCategoryDisplay[bicName] = category.Name;
                }

                symbolCountByFamilyCategory[bicName] = existingCount + 1;

                if (bicName == "OST_MultiCategoryTags")
                {
                    if (collectedSymbols.Add(symbol.Id))
                    {
                        multiCategoryOptions.Add(new TagSymbolOption(symbol, true));
                    }

                    continue;
                }

                if (IsTagCategoryName(bicName))
                {
                    List<FamilySymbol> list;
                    if (!symbolsByTagCategoryName.TryGetValue(bicName, out list))
                    {
                        list = new List<FamilySymbol>();
                        symbolsByTagCategoryName[bicName] = list;
                    }

                    if (collectedSymbols.Add(symbol.Id))
                    {
                        list.Add(symbol);
                    }
                }
            }

            int tagsFromInstances = 0;

            foreach (IndependentTag tag in new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>())
            {
                if (tag == null || !tag.IsValidObject)
                {
                    continue;
                }

                FamilySymbol symbol = tag.GetTypeId() != ElementId.InvalidElementId
                    ? doc.GetElement(tag.GetTypeId()) as FamilySymbol
                    : null;

                if (symbol == null || collectedSymbols.Contains(symbol.Id))
                {
                    continue;
                }

                Category category = GetSymbolCategory(symbol);
                if (category == null)
                {
                    continue;
                }

                string bicName = GetBuiltInCategoryName(category);
                if (string.IsNullOrEmpty(bicName))
                {
                    continue;
                }

                if (bicName == "OST_MultiCategoryTags")
                {
                    if (collectedSymbols.Add(symbol.Id))
                    {
                        multiCategoryOptions.Add(new TagSymbolOption(symbol, true));
                        tagsFromInstances++;
                    }

                    continue;
                }

                if (IsTagCategoryName(bicName))
                {
                    List<FamilySymbol> list;
                    if (!symbolsByTagCategoryName.TryGetValue(bicName, out list))
                    {
                        list = new List<FamilySymbol>();
                        symbolsByTagCategoryName[bicName] = list;
                    }

                    if (collectedSymbols.Add(symbol.Id))
                    {
                        list.Add(symbol);
                        tagsFromInstances++;
                    }
                }
            }

            Logger.Info(
                $"Family symbols in project: {symbolCountByFamilyCategory.Values.Sum()} " +
                $"(unique categories: {symbolCountByFamilyCategory.Count}); " +
                $"tag symbols found via symbols: {symbolsByTagCategoryName.Values.Sum(l => l.Count)}, " +
                $"via instances: +{tagsFromInstances}; multi-category: {multiCategoryOptions.Count}.");

            foreach (KeyValuePair<string, int> pair in symbolCountByFamilyCategory)
            {
                Logger.Debug(
                    $"  Fam cat '{familyCategoryDisplay[pair.Key]}' ({pair.Key}): {pair.Value} symbols.");
            }

            foreach (KeyValuePair<string, List<FamilySymbol>> pair in symbolsByTagCategoryName)
            {
                Logger.Info(
                    $"  Tag category '{pair.Key}': {pair.Value.Count} symbols, " +
                    $"families [{string.Join(", ", pair.Value.Select(s => s.Family.Name).Distinct())}].");
            }

            Dictionary<ElementId, List<TagSymbolOption>> result = new Dictionary<ElementId, List<TagSymbolOption>>();

            foreach (Category category in categoryList)
            {
                if (category == null)
                {
                    continue;
                }

                string bicName = GetBuiltInCategoryName(category);
                if (string.IsNullOrEmpty(bicName))
                {
                    continue;
                }

                HashSet<string> candidateNames = GetTagCategoryCandidates(bicName);
                List<TagSymbolOption> options = new List<TagSymbolOption>();

                foreach (string candidateName in candidateNames)
                {
                    List<FamilySymbol> symbols;
                    if (symbolsByTagCategoryName.TryGetValue(candidateName, out symbols))
                    {
                        options.AddRange(symbols
                            .OrderBy(s => s.Family.Name, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(s => new TagSymbolOption(s, false)));
                    }
                }

                options.AddRange(multiCategoryOptions);

                Logger.Info(
                    $"  Element category '{category.Name}': candidates [{string.Join(", ", candidateNames)}] " +
                    $"-> options {options.Count}.");

                if (options.Count > 0)
                {
                    result[category.Id] = options;
                }
            }

            return result;
        }

        private static HashSet<string> GetTagCategoryCandidates(string elementBicName)
        {
            string[] mapped;
            if (ElementToTagCategoryMap.TryGetValue(elementBicName, out mapped))
            {
                return new HashSet<string>(mapped, StringComparer.Ordinal);
            }

            return new HashSet<string>(StringComparer.Ordinal)
            {
                elementBicName + "Tags"
            };
        }

        private static bool IsTagCategoryName(string bicName)
        {
            return bicName.EndsWith("Tags", StringComparison.Ordinal)
                && !ExcludedTagCategoryNames.Contains(bicName);
        }

        public static bool IsMultiCategoryTag(FamilySymbol symbol)
        {
            Category category = GetSymbolCategory(symbol);
            if (category == null)
            {
                return false;
            }

            string bicName = GetBuiltInCategoryName(category);
            return bicName == "OST_MultiCategoryTags";
        }

        private static Category GetSymbolCategory(FamilySymbol symbol)
        {
            if (symbol == null)
            {
                return null;
            }

            try
            {
                Category category = symbol.Category;
                if (category != null)
                {
                    return category;
                }
            }
            catch
            {
            }

            if (symbol.Family != null)
            {
                try
                {
                    Family family = symbol.Family;
                    if (family.FamilyCategory != null)
                    {
                        return family.FamilyCategory;
                    }
                }
                catch
                {
                }

                try
                {
                    Family family = symbol.Family;
                    if (family.Category != null)
                    {
                        return family.Category;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string GetBuiltInCategoryName(Category category)
        {
            if (category == null)
            {
                return null;
            }

#pragma warning disable CS0618
            BuiltInCategory builtInCategory = (BuiltInCategory)category.Id.IntegerValue;
#pragma warning restore CS0618
            return builtInCategory.ToString();
        }

        public string GetAssemblyNameFromSchedule(Document doc, ViewSchedule schedule)
        {
            if (schedule == null || !schedule.IsValidObject)
            {
                return null;
            }

            string fromFilter = null;
            ScheduleDefinition definition = schedule.Definition;

            for (int i = 0; i < definition.GetFilterCount(); i++)
            {
                ScheduleFilter filter = definition.GetFilter(i);
                if (filter.FilterType != ScheduleFilterType.Equal || !filter.IsStringValue)
                {
                    continue;
                }

                string value = filter.GetStringValue();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fromFilter = value.Trim();
                    break;
                }
            }

            if (fromFilter != null && AssemblyExists(doc, fromFilter))
            {
                return fromFilter;
            }

            const string suffix = ScheduleService.ScheduleSuffix;
            if (schedule.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                string assemblyName = schedule.Name.Substring(0, schedule.Name.Length - suffix.Length);
                if (!string.IsNullOrEmpty(assemblyName) && AssemblyExists(doc, assemblyName))
                {
                    return assemblyName;
                }
            }

            return fromFilter;
        }

        public static bool AssemblyExists(Document doc, string assemblyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .Any(a => a.Name == assemblyName);
        }

        public TagPlacementResult PlaceTags(
            Document doc,
            IEnumerable<ViewSchedule> schedules,
            IReadOnlyDictionary<ElementId, TagSymbolOption> choicesByCategoryId,
            int minOffsetMm,
            int zoneHeightMm,
            bool textBelowShelf)
        {
            TagPlacementResult result = new TagPlacementResult();
            Schema tagSchema = GetTagSchema();

            List<IndependentTag> pluginTags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => HasPluginEntity(t, tagSchema))
                .ToList();

            foreach (ViewSchedule schedule in schedules)
            {
                if (schedule == null || !schedule.IsValidObject)
                {
                    continue;
                }

                string assemblyName = GetAssemblyNameFromSchedule(doc, schedule);
                if (assemblyName == null)
                {
                    result.Warnings.Add($"Спецификация «{schedule.Name}»: не удалось определить сборку.");
                    continue;
                }

                List<View> views = _viewService
                    .GetExistingAssemblyViews(doc, assemblyName)
                    .Where(v => v is ViewPlan || v is ViewSection)
                    .ToList();

                if (views.Count == 0)
                {
                    result.Warnings.Add($"Сборка «{assemblyName}»: планы/разрезы не найдены.");
                    continue;
                }

                List<Element> elements = CollectScheduleElements(doc, schedule);
                List<Element> elementsToTag = elements
                    .Where(e => e.Category != null && choicesByCategoryId.ContainsKey(e.Category.Id))
                    .ToList();

                if (elementsToTag.Count == 0)
                {
                    continue;
                }

                int deletedInSchedule = DeletePluginTagsInViews(doc, pluginTags, views, tagSchema);
                result.TagsDeletedCount += deletedInSchedule;

                foreach (View view in views)
                {
                    int previousCreated = result.TagsCreatedCount;
                    PlaceTagsInView(doc, schedule, view, elementsToTag, choicesByCategoryId, result, tagSchema,
                        minOffsetMm, zoneHeightMm, textBelowShelf);

                    if (result.TagsCreatedCount > previousCreated)
                    {
                        result.TaggedViewsCount++;
                    }
                }
            }

            return result;
        }

        private int DeletePluginTagsInViews(
            Document doc,
            IList<IndependentTag> pluginTags,
            IEnumerable<View> views,
            Schema tagSchema)
        {
            HashSet<ElementId> viewIds = new HashSet<ElementId>(views.Select(v => v.Id));
            HashSet<ElementId> tagsToDelete = new HashSet<ElementId>();

            foreach (IndependentTag tag in pluginTags)
            {
                if (tag == null || !tag.IsValidObject)
                {
                    continue;
                }

                if (viewIds.Contains(tag.OwnerViewId))
                {
                    tagsToDelete.Add(tag.Id);
                }
            }

            if (tagsToDelete.Count > 0)
            {
                Logger.Info($"Deleting {tagsToDelete.Count} plugin tags in views {string.Join(", ", viewIds)}.");
                doc.Delete(tagsToDelete.ToList());
            }

            return tagsToDelete.Count;
        }

        private void PlaceTagsInView(
            Document doc,
            ViewSchedule schedule,
            View view,
            IList<Element> elementsToTag,
            IReadOnlyDictionary<ElementId, TagSymbolOption> choicesByCategoryId,
            TagPlacementResult result,
            Schema tagSchema,
            int minOffsetMm,
            int zoneHeightMm,
            bool textBelowShelf)
        {
            double scaleRatio = view.Scale / 100.0;
            double minOffsetFt = minOffsetMm / 304.8;
            double zoneHeightFt = zoneHeightMm / 304.8 * scaleRatio;
            double margin = HeadMargin * scaleRatio;

            double halfUp = (textBelowShelf ? zoneHeightFt / 2 : zoneHeightFt) + margin;
            double halfDown = (textBelowShelf ? zoneHeightFt / 2 : 0) + margin;
            double stepFt = (halfUp + halfDown) / Math.Sqrt(0.5);

            List<Rect2D> placedHeads = new List<Rect2D>();

            foreach (Element element in elementsToTag)
            {
                TagSymbolOption option;
                if (element.Category == null
                    || !choicesByCategoryId.TryGetValue(element.Category.Id, out option))
                {
                    continue;
                }

                if (!HasPositionValue(element))
                {
                    result.ElementsSkippedCount++;
                    Logger.Debug($"Element {element.Id}: no position value, skip.");
                    continue;
                }

                if (ElementLooksAtView(element, view))
                {
                    result.ElementsCutSkippedCount++;
                    Logger.Debug($"Element {element.Id}: connectors point to viewer in view '{view.Name}', skip.");
                    continue;
                }

                Element lookingAncestor = FindAncestorLookingAtView(element, view);
                if (lookingAncestor != null)
                {
                    result.ElementsCutSkippedCount++;
                    Logger.Debug(
                        $"Element {element.Id}: ancestor {lookingAncestor.Id} looks at viewer in view '{view.Name}', skip.");
                    continue;
                }

                try
                {
                    XYZ anchor = GetElementAnchor(element, view);
                    if (anchor == null)
                    {
                        result.ElementsSkippedCount++;
                        Logger.Debug($"Element {element.Id}: not visible in view '{view.Name}', skip.");
                        continue;
                    }

                    XYZ right = view.RightDirection;
                    XYZ up = view.UpDirection;
                    double anchorRight = anchor.X * right.X + anchor.Y * right.Y + anchor.Z * right.Z;
                    double anchorUp = anchor.X * up.X + anchor.Y * up.Y + anchor.Z * up.Z;

                    TagMode tagMode = option.IsMultiCategory
                        ? TagMode.TM_ADDBY_MULTICATEGORY
                        : TagMode.TM_ADDBY_CATEGORY;

                    IndependentTag tag = IndependentTag.Create(
                        doc, view.Id, new Reference(element), true, tagMode,
                        TagOrientation.Horizontal, anchor);

                    if (tag == null)
                    {
                        result.ElementsSkippedCount++;
                        Logger.Warn($"Tag creation failed (null) for element {element.Id} in view '{view.Name}'.");
                        continue;
                    }

                    tag.ChangeTypeId(option.Symbol.Id);

                    Entity entity = new Entity(tagSchema);
                    entity.Set(AssemblyNameField, schedule.Name);
                    tag.SetEntity(entity);

                    tag.LeaderEndCondition = LeaderEndCondition.Free;

#if REVIT2022_OR_GREATER
                    tag.SetLeaderEnd(new Reference(element), anchor);
#else
                    tag.LeaderEnd = anchor;
#endif

                    TagDims tagDims = GetTagDimensions(view, option.Symbol);
                    bool dimsKnown = tagDims.Width > 0 || tagDims.Height > 0;

                    HeadDirection direction = DirRightUp;
                    double offsetFt = minOffsetFt;
                    bool found = false;

                    foreach (KeyValuePair<HeadDirection, double> candidate in BuildCandidateOffsets(minOffsetFt, stepFt))
                    {
                        double candidateRight = anchorRight + candidate.Key.Dx * candidate.Value;
                        double candidateUp = anchorUp + candidate.Key.Dy * candidate.Value;

                        if (!RectsIntersectAny(
                                BuildHeadRect(candidateRight, candidateUp, tagDims, dimsKnown, halfUp, halfDown, margin),
                                placedHeads))
                        {
                            direction = candidate.Key;
                            offsetFt = candidate.Value;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        direction = DirRightUp;
                        offsetFt = minOffsetFt;
                        Logger.Warn(
                            $"Element {element.Id}: no free tag head position in view '{view.Name}', " +
                            "using default position (overlap possible).");
                    }

                    double headRight = anchorRight + direction.Dx * offsetFt;
                    double headUp = anchorUp + direction.Dy * offsetFt;

                    double elbowRight;
                    if (!dimsKnown)
                    {
                        elbowRight = headRight;
                    }
                    else if (direction.Dx < 0)
                    {
                        elbowRight = headRight + (Math.Max(tagDims.RightGap, 0) + ElbowMargin);
                    }
                    else
                    {
                        elbowRight = headRight - (tagDims.LeftGap + ElbowMargin);
                    }

                    XYZ head = anchor + right * (headRight - anchorRight) + up * (headUp - anchorUp);
                    XYZ elbow = anchor + right * (elbowRight - anchorRight) + up * (headUp - anchorUp);

#if REVIT2022_OR_GREATER
                    tag.SetLeaderElbow(new Reference(element), elbow);
#else
                    tag.LeaderElbow = elbow;
#endif
                    tag.TagHeadPosition = head;

                    placedHeads.Add(BuildHeadRect(headRight, headUp, tagDims, dimsKnown, halfUp, halfDown, margin));

                    result.TagsCreatedCount++;
                    Logger.Info(
                        $"Tag {tag.Id} placed in view '{view.Name}' for element {element.Id}, " +
                        $"symbol '{option.Symbol.Family.Name}:{option.Symbol.Name}', head ({head.X:F2}, {head.Y:F2}), " +
                        $"offset {offsetFt * 304.8:F0} mm, direction {direction.Name}, scale 1:{view.Scale}.");
                }
                catch (Exception ex)
                {
                    result.ElementsSkippedCount++;
                    Logger.Warn(
                        $"Could not tag element {element.Id} in view '{view.Name}': {ex.Message}");
                }
            }
        }

        private static Element FindAncestorLookingAtView(Element element, View view)
        {
            Element current = element;

            while (current is FamilyInstance familyInstance)
            {
                Element superComponent;
                try
                {
                    superComponent = familyInstance.SuperComponent;
                }
                catch
                {
                    break;
                }

                if (superComponent == null)
                {
                    break;
                }

                if (ElementLooksAtView(superComponent, view))
                {
                    return superComponent;
                }

                current = superComponent;
            }

            return null;
        }

        private sealed class HeadDirection
        {
            public readonly double Dx;
            public readonly double Dy;
            public readonly string Name;

            public HeadDirection(double dx, double dy, string name)
            {
                Dx = dx;
                Dy = dy;
                Name = name;
            }
        }

        private static readonly HeadDirection DirRightUp =
            new HeadDirection(Math.Sqrt(0.5), Math.Sqrt(0.5), "right-up");
        private static readonly HeadDirection DirLeftUp =
            new HeadDirection(-Math.Sqrt(0.5), Math.Sqrt(0.5), "left-up");
        private static readonly HeadDirection DirRightDown =
            new HeadDirection(Math.Sqrt(0.5), -Math.Sqrt(0.5), "right-down");
        private static readonly HeadDirection DirLeftDown =
            new HeadDirection(-Math.Sqrt(0.5), -Math.Sqrt(0.5), "left-down");

        private static List<KeyValuePair<HeadDirection, double>> BuildCandidateOffsets(double minOffsetFt, double stepFt)
        {
            List<KeyValuePair<HeadDirection, double>> candidates =
                new List<KeyValuePair<HeadDirection, double>>();

            HeadDirection[] diagonals = { DirRightUp, DirLeftUp, DirRightDown, DirLeftDown };

            for (int step = 0; step <= MaxLadderSteps; step++)
            {
                double d = minOffsetFt + step * stepFt;

                foreach (HeadDirection direction in diagonals)
                {
                    candidates.Add(new KeyValuePair<HeadDirection, double>(direction, d));
                }
            }

            return candidates;
        }

        private static Rect2D BuildHeadRect(
            double headRight,
            double headUp,
            TagDims dims,
            bool dimsKnown,
            double halfUp,
            double halfDown,
            double margin)
        {
            double halfLeft = dimsKnown ? Math.Max(dims.LeftGap, 0) + margin : margin;
            double halfRight = dimsKnown ? Math.Max(dims.RightGap, 0) + margin : margin;

            return new Rect2D
            {
                MinX = headRight - halfLeft,
                MaxX = headRight + halfRight,
                MinY = headUp - halfDown,
                MaxY = headUp + halfUp
            };
        }

        private static bool RectsIntersectAny(Rect2D rect, List<Rect2D> rects)
        {
            foreach (Rect2D other in rects)
            {
                if (RectsIntersect(rect, other))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RectsIntersect(Rect2D a, Rect2D b)
        {
            return a.MinX < b.MaxX
                && a.MaxX > b.MinX
                && a.MinY < b.MaxY
                && a.MaxY > b.MinY;
        }

        private static bool HasPluginEntity(IndependentTag tag, Schema schema)
        {
            try
            {
                Entity entity = tag.GetEntity(schema);
                return entity != null && entity.IsValid();
            }
            catch
            {
                return false;
            }
        }

        private static bool ElementLooksAtView(Element element, View view)
        {
            ConnectorSet connectors = null;

            try
            {
                FamilyInstance familyInstance = element as FamilyInstance;
                if (familyInstance != null && familyInstance.MEPModel != null)
                {
                    connectors = familyInstance.MEPModel.ConnectorManager.Connectors;
                }
                else
                {
                    MEPCurve mepCurve = element as MEPCurve;
                    if (mepCurve != null)
                    {
                        connectors = mepCurve.ConnectorManager.Connectors;
                    }
                }
            }
            catch
            {
            }

            if (connectors == null || connectors.Size == 0)
            {
                return false;
            }

            XYZ viewDirection = view.ViewDirection;
            if (viewDirection == null)
            {
                return false;
            }

            try
            {
                int processed = 0;

                foreach (Connector connector in connectors)
                {
                    processed++;

                    try
                    {
                        XYZ zAxis = connector.CoordinateSystem != null
                            ? connector.CoordinateSystem.BasisZ
                            : null;

                        if (zAxis == null)
                        {
                            continue;
                        }

                        double dot = Math.Abs(
                            zAxis.X * viewDirection.X
                            + zAxis.Y * viewDirection.Y
                            + zAxis.Z * viewDirection.Z);

                        if (dot < 0.95)
                        {
                            return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }

                return processed > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasPositionValue(Element element)
        {
            Parameter positionParameter = element.LookupParameter(PositionParameterName);
            if (positionParameter == null || !positionParameter.HasValue)
            {
                return false;
            }

            if (positionParameter.StorageType == StorageType.String)
            {
                string value = positionParameter.AsString();
                return !string.IsNullOrEmpty(value);
            }

            if (positionParameter.StorageType == StorageType.Integer)
            {
                int value = positionParameter.AsInteger();
                return value != 0;
            }

            return true;
        }

        private static XYZ GetElementAnchor(Element element, View view)
        {
            BoundingBoxXYZ boundingBox = null;

            try
            {
                boundingBox = element.get_BoundingBox(view);
            }
            catch
            {
            }

            if (boundingBox == null
                || boundingBox.Min == null
                || boundingBox.Max == null
                || !IsFinite(boundingBox.Min)
                || !IsFinite(boundingBox.Max))
            {
                return null;
            }

            return (boundingBox.Min + boundingBox.Max) / 2;
        }

        private static bool IsFinite(XYZ point)
        {
            return point != null
                && !double.IsNaN(point.X)
                && !double.IsNaN(point.Y)
                && !double.IsNaN(point.Z)
                && !double.IsInfinity(point.X)
                && !double.IsInfinity(point.Y)
                && !double.IsInfinity(point.Z);
        }

        public void PrepareTagDimensions(
            Document doc,
            IEnumerable<ViewSchedule> schedules,
            IReadOnlyDictionary<ElementId, TagSymbolOption> choices)
        {
            if (doc == null || schedules == null || choices == null || choices.Count == 0)
            {
                return;
            }

            Dictionary<string, Tuple<View, Element, FamilySymbol>> toMeasure =
                new Dictionary<string, Tuple<View, Element, FamilySymbol>>(StringComparer.Ordinal);

            foreach (ViewSchedule schedule in schedules)
            {
                if (schedule == null || !schedule.IsValidObject)
                {
                    continue;
                }

                string assemblyName = GetAssemblyNameFromSchedule(doc, schedule);
                if (assemblyName == null)
                {
                    continue;
                }

                List<View> views = _viewService
                    .GetExistingAssemblyViews(doc, assemblyName)
                    .Where(v => v is ViewPlan || v is ViewSection)
                    .ToList();

                if (views.Count == 0)
                {
                    continue;
                }

                List<Element> elements = CollectScheduleElements(doc, schedule)
                    .Where(e => e.Category != null && choices.ContainsKey(e.Category.Id))
                    .ToList();

                foreach (View view in views)
                {
                    foreach (Element element in elements)
                    {
                        TagSymbolOption option;
                        if (!choices.TryGetValue(element.Category.Id, out option))
                        {
                            continue;
                        }

#pragma warning disable CS0618
                        string key = option.Symbol.Id.IntegerValue + "|" + view.Id.IntegerValue;
#pragma warning restore CS0618

                        if (_tagDimsCache.ContainsKey(key))
                        {
                            continue;
                        }

                        if (!toMeasure.ContainsKey(key))
                        {
                            toMeasure[key] = Tuple.Create(view, element, option.Symbol);
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, Tuple<View, Element, FamilySymbol>> pair in toMeasure)
            {
                MeasureTagDimensions(doc, pair.Value.Item1, pair.Value.Item2, pair.Value.Item3);
            }
        }

        private void MeasureTagDimensions(Document doc, View view, Element element, FamilySymbol symbol)
        {
#pragma warning disable CS0618
            string key = symbol.Id.IntegerValue + "|" + view.Id.IntegerValue;
#pragma warning restore CS0618

            if (_tagDimsCache.ContainsKey(key))
            {
                return;
            }

            try
            {
                XYZ anchor = GetElementAnchor(element, view);
                if (anchor == null)
                {
                    return;
                }

                XYZ right = view.RightDirection;
                XYZ up = view.UpDirection;

                TransactionGroup group = new TransactionGroup(doc, "Пробник габарита марки");
                group.Start();

                try
                {
                    IndependentTag probe = null;

                    using (Transaction transaction = new Transaction(doc, "Измерить марку"))
                    {
                        transaction.Start();

                        TagMode tagMode = IsMultiCategoryTag(symbol)
                            ? TagMode.TM_ADDBY_MULTICATEGORY
                            : TagMode.TM_ADDBY_CATEGORY;

                        probe = IndependentTag.Create(
                            doc, view.Id, new Reference(element), true, tagMode,
                            TagOrientation.Horizontal, anchor);

                        if (probe != null)
                        {
                            probe.ChangeTypeId(symbol.Id);
                            probe.LeaderEndCondition = LeaderEndCondition.Free;

                            XYZ leaderEnd;
#if REVIT2022_OR_GREATER
                            leaderEnd = probe.GetLeaderEnd(new Reference(element));
#else
                            leaderEnd = probe.LeaderEnd;
#endif
                            probe.TagHeadPosition = leaderEnd;
#if REVIT2022_OR_GREATER
                            probe.SetLeaderElbow(new Reference(element), leaderEnd);
#else
                            probe.LeaderElbow = leaderEnd;
#endif
                        }

                        transaction.Commit();
                    }

                    if (probe != null)
                    {
                        BoundingBoxXYZ boundingBox = probe.get_BoundingBox(view);
                        XYZ headPosition = probe.TagHeadPosition;

                        if (boundingBox != null && boundingBox.Min != null && boundingBox.Max != null
                            && headPosition != null)
                        {
                            Rect2D rect = ProjectWorldBox(boundingBox, right, up);

                            double headX = headPosition.X * right.X + headPosition.Y * right.Y + headPosition.Z * right.Z;
                            double headY = headPosition.X * up.X + headPosition.Y * up.Y + headPosition.Z * up.Z;

                            TagDims dims = new TagDims
                            {
                                Width = rect.Width,
                                Height = rect.Height,
                                LeftGap = Math.Max(0, headX - rect.MinX),
                                RightGap = Math.Max(0, rect.MaxX - headX),
                                DownGap = Math.Max(0, headY - rect.MinY),
                                UpGap = Math.Max(0, rect.MaxY - headY)
                            };

                            _tagDimsCache[key] = dims;

                            Logger.Info(
                                $"Tag dims measured for '{symbol.Family.Name}:{symbol.Name}' in view '{view.Name}': " +
                                $"{dims.Width:F2} x {dims.Height:F2} ft (L {dims.LeftGap:F2}, R {dims.RightGap:F2}, " +
                                $"D {dims.DownGap:F2}, U {dims.UpGap:F2}).");
                        }
                    }
                }
                finally
                {
                    group.RollBack();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not measure tag dims for '{symbol.Name}': {ex.Message}");
            }
        }

        private TagDims GetTagDimensions(View view, FamilySymbol symbol)
        {
#pragma warning disable CS0618
            string key = symbol.Id.IntegerValue + "|" + view.Id.IntegerValue;
#pragma warning restore CS0618

            TagDims cached;
            if (_tagDimsCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            return default;
        }

        private struct Rect2D
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public double Width
            {
                get { return MaxX - MinX; }
            }

            public double Height
            {
                get { return MaxY - MinY; }
            }
        }

        private static Rect2D ProjectWorldBox(BoundingBoxXYZ box, XYZ right, XYZ up)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            double[] xs = { box.Min.X, box.Max.X };
            double[] ys = { box.Min.Y, box.Max.Y };
            double[] zs = { box.Min.Z, box.Max.Z };

            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        XYZ world = box.Transform.OfPoint(new XYZ(xs[i], ys[j], zs[k]));

                        double px = world.X * right.X + world.Y * right.Y + world.Z * right.Z;
                        double py = world.X * up.X + world.Y * up.Y + world.Z * up.Z;

                        minX = Math.Min(minX, px);
                        maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py);
                        maxY = Math.Max(maxY, py);
                    }
                }
            }

            return new Rect2D { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY };
        }
    }
}
