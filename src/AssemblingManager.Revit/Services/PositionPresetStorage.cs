using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using AssemblingManager.Core.Common;

namespace AssemblingManager.Revit.Services
{
    public static class PositionPresetStorage
    {
        private static readonly Guid SchemaGuid =
            new Guid("3B4F6F87-1A2C-4E8D-9B21-6C5D8E7A0F43");

        private const string KeywordsField = "Keywords";

        public const string PresetSeparator = ";";

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
            builder.SetSchemaName("AssemblingManagerPositionKeywordsPreset");
            builder.AddSimpleField(KeywordsField, typeof(string));
            return builder.Finish();
        }

        public static List<string> ReadPresetKeywords(Document doc)
        {
            List<string> result = new List<string>();

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
                        string value = entity.Get<string>(schema.GetField(KeywordsField));
                        if (!string.IsNullOrEmpty(value))
                        {
                            result = value
                                .Split(new[] { PresetSeparator }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();

                            Logger.Info($"Position preset loaded: {string.Join(", ", result)}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read position preset: {ex.Message}");
            }

            return result;
        }

        public static void SavePresetKeywords(Document doc, IEnumerable<string> keywords)
        {
            if (doc == null)
            {
                return;
            }

            string value = string.Join(PresetSeparator, keywords);

            try
            {
                Schema schema = GetSchema();

                using (Transaction transaction = new Transaction(doc, "Сохранить пресет позиций"))
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
                    presetEntity.Set(KeywordsField, value);
                    dataStorage.SetEntity(presetEntity);

                    transaction.Commit();
                }

                Logger.Info($"Position preset saved: '{value}'.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not save position preset: {ex.Message}");
            }
        }
    }
}
