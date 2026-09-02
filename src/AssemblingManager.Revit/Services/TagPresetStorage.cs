using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using AssemblingManager.Core.Common;

namespace AssemblingManager.Revit.Services
{
    public static class TagPresetStorage
    {
        private static readonly Guid SchemaGuid =
            new Guid("8C41D2E6-63F7-4B19-A5D4-0E9C7B6A3F52");

        private const string EntriesField = "Entries";
        private const string MinOffsetField = "MinOffsetMm";
        private const string ZoneHeightField = "ZoneHeightMm";
        private const string TextBelowField = "TextBelowShelf";

        public const string EntrySeparator = "|";
        public const int DefaultMinOffsetMm = 500;
        public const int DefaultZoneHeightMm = 300;

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);

            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("AssemblingManagerPositionTagPreset");
            builder.AddArrayField(EntriesField, typeof(string));
            builder.AddSimpleField(MinOffsetField, typeof(int));
            builder.AddSimpleField(ZoneHeightField, typeof(int));
            builder.AddSimpleField(TextBelowField, typeof(bool));
            return builder.Finish();
        }

        public static Dictionary<string, string> ReadPreset(Document doc)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (doc == null)
            {
                return result;
            }

            try
            {
                Schema schema = GetSchema();

                foreach (DataStorage dataStorage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                {
                    Entity entity = dataStorage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        IList<string> entries = entity.Get<IList<string>>(schema.GetField(EntriesField));
                        if (entries != null)
                        {
                            foreach (string entry in entries)
                            {
                                if (string.IsNullOrEmpty(entry))
                                {
                                    continue;
                                }

                                string[] parts = entry.Split(new[] { EntrySeparator }, StringSplitOptions.None);
                                if (parts.Length != 3)
                                {
                                    continue;
                                }

                                result[parts[0]] = parts[1] + EntrySeparator + parts[2];
                            }
                        }

                        Logger.Info($"Tag preset loaded: {result.Count} entries.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read tag preset: {ex.Message}");
            }

            return result;
        }

        public static int ReadMinOffsetMm(Document doc)
        {
            if (doc == null)
            {
                return DefaultMinOffsetMm;
            }

            try
            {
                Schema schema = GetSchema();

                foreach (DataStorage dataStorage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                {
                    Entity entity = dataStorage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        int value = entity.Get<int>(schema.GetField(MinOffsetField));
                        if (value > 0)
                        {
                            Logger.Info($"Tag preset min offset loaded: {value} mm.");
                            return value;
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read tag preset min offset: {ex.Message}");
            }

            return DefaultMinOffsetMm;
        }

        public static int ReadZoneHeightMm(Document doc)
        {
            if (doc == null)
            {
                return DefaultZoneHeightMm;
            }

            try
            {
                Schema schema = GetSchema();

                foreach (DataStorage dataStorage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                {
                    Entity entity = dataStorage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        int value = entity.Get<int>(schema.GetField(ZoneHeightField));
                        if (value > 0)
                        {
                            Logger.Info($"Tag preset zone height loaded: {value} mm.");
                            return value;
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read tag preset zone height: {ex.Message}");
            }

            return DefaultZoneHeightMm;
        }

        public static bool ReadTextBelowShelf(Document doc)
        {
            if (doc == null)
            {
                return false;
            }

            try
            {
                Schema schema = GetSchema();

                foreach (DataStorage dataStorage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                {
                    Entity entity = dataStorage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        bool value = entity.Get<bool>(schema.GetField(TextBelowField));
                        Logger.Info($"Tag preset text-below-shelf loaded: {value}.");
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read tag preset text-below-shelf: {ex.Message}");
            }

            return false;
        }

        public static void SavePreset(
            Document doc,
            IEnumerable<KeyValuePair<string, string>> preset,
            int minOffsetMm,
            int zoneHeightMm,
            bool textBelowShelf)
        {
            if (doc == null)
            {
                return;
            }

            List<string> entries = preset
                .Where(p => !string.IsNullOrEmpty(p.Key) && !string.IsNullOrEmpty(p.Value))
                .Select(p => p.Key + EntrySeparator + p.Value)
                .ToList();

            try
            {
                Schema schema = GetSchema();

                using (Transaction transaction = new Transaction(doc, "Сохранить пресет марок"))
                {
                    transaction.Start();

                    DataStorage dataStorage = null;

                    foreach (DataStorage candidate in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                    {
                        Entity entity = candidate.GetEntity(schema);
                        if (entity != null && entity.IsValid())
                        {
                            dataStorage = candidate;
                            break;
                        }
                    }

                    if (dataStorage == null)
                    {
                        dataStorage = DataStorage.Create(doc);
                    }

                    Entity presetEntity = new Entity(schema);
                    presetEntity.Set<IList<string>>(EntriesField, entries);
                    presetEntity.Set(MinOffsetField, minOffsetMm);
                    presetEntity.Set(ZoneHeightField, zoneHeightMm);
                    presetEntity.Set(TextBelowField, textBelowShelf);
                    dataStorage.SetEntity(presetEntity);

                    transaction.Commit();
                }

                Logger.Info(
                    $"Tag preset saved: {entries.Count} entries, min offset {minOffsetMm} mm, " +
                    $"zone height {zoneHeightMm} mm, text below shelf {textBelowShelf}.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not save tag preset: {ex.Message}");
            }
        }
    }
}
