using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using AssemblingManager.Core.Common;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public static class NeighborAxesPresetStorage
    {
        private static readonly Guid SchemaGuid =
            new Guid("C8E24A61-7D93-4B5F-A0E1-6F4C9B8D2A17");

        private const string RadiusField = "RadiusMm";
        private const string GridWorksetNameField = "GridWorksetName";

        public static NeighborAxesSettings ReadSettings(Document doc)
        {
            NeighborAxesSettings settings = new NeighborAxesSettings();

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
                        string radiusText = entity.Get<string>(schema.GetField(RadiusField));
                        double radius;
                        if (double.TryParse(radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out radius) && radius > 0)
                        {
                            settings.RadiusMm = radius;
                        }

                        string worksetName = entity.Get<string>(schema.GetField(GridWorksetNameField));
                        if (!string.IsNullOrWhiteSpace(worksetName))
                        {
                            settings.GridWorksetName = worksetName;
                        }

                        Logger.Info(
                            $"Neighbor axes preset loaded: radius {settings.RadiusMm} mm, " +
                            $"grid workset '{settings.GridWorksetName ?? "<all>"}'.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read neighbor axes preset: {ex.Message}");
            }

            return settings;
        }

        public static void SaveSettings(Document doc, NeighborAxesSettings settings)
        {
            if (doc == null || settings == null)
            {
                return;
            }

            try
            {
                Schema schema = GetSchema();

                using (Transaction transaction = new Transaction(doc, "Сохранить пресет ближайших осей"))
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
                    presetEntity.Set(RadiusField, settings.RadiusMm.ToString(CultureInfo.InvariantCulture));
                    presetEntity.Set(GridWorksetNameField, settings.GridWorksetName ?? string.Empty);
                    dataStorage.SetEntity(presetEntity);

                    transaction.Commit();
                }

                Logger.Info(
                    $"Neighbor axes preset saved: radius {settings.RadiusMm} mm, " +
                    $"grid workset '{settings.GridWorksetName ?? "<all>"}'.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not save neighbor axes preset: {ex.Message}");
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
            builder.SetSchemaName("AssemblingManagerNeighborAxesPreset");
            builder.AddSimpleField(RadiusField, typeof(string));
            builder.AddSimpleField(GridWorksetNameField, typeof(string));
            return builder.Finish();
        }
    }
}
