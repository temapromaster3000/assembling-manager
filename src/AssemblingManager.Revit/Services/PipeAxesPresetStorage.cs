using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public static class PipeAxesPresetStorage
    {
        private static readonly Guid SchemaGuid =
            new Guid("9B4F2C7E-3A18-4D26-B5E9-7C61A0F8D345");

        private const string LineStyleNameField = "LineStyleName";
        private const string SkipOccludedField = "SkipOccluded";

        public static PipeAxisSettings ReadSettings(Document doc)
        {
            PipeAxisSettings settings = new PipeAxisSettings();

            if (doc == null)
            {
                return settings;
            }

            try
            {
                Schema schema = GetSchema();

                foreach (DataStorage dataStorage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                {
                    Entity entity = dataStorage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        string lineStyleName = entity.Get<string>(schema.GetField(LineStyleNameField));
                        if (!string.IsNullOrWhiteSpace(lineStyleName))
                        {
                            settings.LineStyleName = lineStyleName;
                        }

                        Field skipField = schema.GetField(SkipOccludedField);
                        if (skipField != null)
                        {
                            string skipText = entity.Get<string>(skipField);
                            if (bool.TryParse(skipText, out bool skipOccluded))
                            {
                                settings.SkipOccludedSegments = skipOccluded;
                            }
                        }

                        Logger.Info(
                            $"Pipe axes preset loaded: line style '{settings.LineStyleName ?? "<not set>"}', " +
                            $"skip occluded = {settings.SkipOccludedSegments}.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read pipe axes preset: {ex.Message}");
            }

            return settings;
        }

        public static void SaveSettings(Document doc, PipeAxisSettings settings)
        {
            if (doc == null || settings == null)
            {
                return;
            }

            try
            {
                Schema schema = GetSchema();

                using (Transaction transaction = new Transaction(doc, "Сохранить пресет осей трубопроводов"))
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
                    presetEntity.Set(LineStyleNameField, settings.LineStyleName ?? string.Empty);

                    Field skipField = schema.GetField(SkipOccludedField);
                    if (skipField != null)
                    {
                        presetEntity.Set(SkipOccludedField, settings.SkipOccludedSegments.ToString());
                    }

                    dataStorage.SetEntity(presetEntity);

                    transaction.Commit();
                }

                Logger.Info(
                    $"Pipe axes preset saved: line style '{settings.LineStyleName ?? "<not set>"}', " +
                    $"skip occluded = {settings.SkipOccludedSegments}.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not save pipe axes preset: {ex.Message}");
            }
        }

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
            builder.SetSchemaName("AssemblingManagerPipeAxesPreset");
            builder.AddSimpleField(LineStyleNameField, typeof(string));

            // Поле могло не существовать в схеме, зарегистрированной старой версией
            // плагина в текущем документе, — читаем и пишем его защитно.
            if (Schema.Lookup(SchemaGuid) == null)
            {
                builder.AddSimpleField(SkipOccludedField, typeof(string));
            }

            return builder.Finish();
        }
    }
}
