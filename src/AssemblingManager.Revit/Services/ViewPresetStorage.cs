using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public static class ViewPresetStorage
    {
        private static readonly Guid SchemaGuid =
            new Guid("D5A1F8C2-4E63-4B7D-92AB-8C30F1E67B54");

        private const string UseGroupingField = "UseGrouping";
        private const string CreatePlanField = "CreatePlan";
        private const string CreateFrontViewField = "CreateFrontView";
        private const string CreateBackViewField = "CreateBackView";
        private const string CreateRightViewField = "CreateRightView";
        private const string CreateLeftViewField = "CreateLeftView";
        private const string Create3DField = "Create3D";
        private const string CreateScheduleField = "CreateSchedule";
        private const string PlanTemplateIdField = "PlanTemplateId";
        private const string SectionTemplateIdField = "SectionTemplateId";
        private const string View3DTemplateIdField = "View3DTemplateId";
        private const string MasterScheduleIdField = "MasterScheduleId";
        private const string ScheduleViewTemplateIdField = "ScheduleViewTemplateId";
        private const string PlanViewFamilyTypeIdField = "PlanViewFamilyTypeId";
        private const string SectionViewFamilyTypeIdField = "SectionViewFamilyTypeId";
        private const string View3DViewFamilyTypeIdField = "View3DViewFamilyTypeId";

        public static ViewCreationOptions ReadOptions(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            try
            {
                Schema schema = GetSchema();

                foreach (DataStorage dataStorage in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>())
                {
                    Entity entity = dataStorage.GetEntity(schema);
                    if (entity == null || !entity.IsValid())
                    {
                        continue;
                    }

                    bool useGrouping = entity.Get<bool>(schema.GetField(UseGroupingField));

                    ViewCreationOptions options = new ViewCreationOptions
                    {
                        UseExistingGroupingParameter = useGrouping,
                        CreateNewParameter = !useGrouping,
                        CreatePlan = entity.Get<bool>(schema.GetField(CreatePlanField)),
                        CreateFrontView = entity.Get<bool>(schema.GetField(CreateFrontViewField)),
                        CreateBackView = entity.Get<bool>(schema.GetField(CreateBackViewField)),
                        CreateRightView = entity.Get<bool>(schema.GetField(CreateRightViewField)),
                        CreateLeftView = entity.Get<bool>(schema.GetField(CreateLeftViewField)),
                        Create3D = entity.Get<bool>(schema.GetField(Create3DField)),
                        CreateSchedule = entity.Get<bool>(schema.GetField(CreateScheduleField)),
                        PlanTemplateId = ReadOptionalId(entity, schema, PlanTemplateIdField),
                        SectionTemplateId = ReadOptionalId(entity, schema, SectionTemplateIdField),
                        View3DTemplateId = ReadOptionalId(entity, schema, View3DTemplateIdField),
                        MasterScheduleId = ReadOptionalId(entity, schema, MasterScheduleIdField),
                        ScheduleViewTemplateId = ReadOptionalId(entity, schema, ScheduleViewTemplateIdField),
                        PlanViewFamilyTypeId = ReadOptionalId(entity, schema, PlanViewFamilyTypeIdField),
                        SectionViewFamilyTypeId = ReadOptionalId(entity, schema, SectionViewFamilyTypeIdField),
                        View3DViewFamilyTypeId = ReadOptionalId(entity, schema, View3DViewFamilyTypeIdField)
                    };

                    Logger.Info(
                        $"View preset loaded: plan={options.CreatePlan}, " +
                        $"sections={options.CreateFrontView}/{options.CreateBackView}/{options.CreateRightView}/{options.CreateLeftView}, " +
                        $"3D={options.Create3D}, schedule={options.CreateSchedule}, use grouping={useGrouping}.");

                    return options;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not read view preset: {ex.Message}");
            }

            return null;
        }

        public static void SaveOptions(Document doc, ViewCreationOptions options)
        {
            if (doc == null || options == null)
            {
                return;
            }

            try
            {
                Schema schema = GetSchema();

                using (Transaction transaction = new Transaction(doc, "Сохранить пресет видов"))
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
                    presetEntity.Set(UseGroupingField, options.UseExistingGroupingParameter);
                    presetEntity.Set(CreatePlanField, options.CreatePlan);
                    presetEntity.Set(CreateFrontViewField, options.CreateFrontView);
                    presetEntity.Set(CreateBackViewField, options.CreateBackView);
                    presetEntity.Set(CreateRightViewField, options.CreateRightView);
                    presetEntity.Set(CreateLeftViewField, options.CreateLeftView);
                    presetEntity.Set(Create3DField, options.Create3D);
                    presetEntity.Set(CreateScheduleField, options.CreateSchedule);
                    presetEntity.Set(PlanTemplateIdField, options.PlanTemplateId ?? 0);
                    presetEntity.Set(SectionTemplateIdField, options.SectionTemplateId ?? 0);
                    presetEntity.Set(View3DTemplateIdField, options.View3DTemplateId ?? 0);
                    presetEntity.Set(MasterScheduleIdField, options.MasterScheduleId ?? 0);
                    presetEntity.Set(ScheduleViewTemplateIdField, options.ScheduleViewTemplateId ?? 0);
                    presetEntity.Set(PlanViewFamilyTypeIdField, options.PlanViewFamilyTypeId ?? 0);
                    presetEntity.Set(SectionViewFamilyTypeIdField, options.SectionViewFamilyTypeId ?? 0);
                    presetEntity.Set(View3DViewFamilyTypeIdField, options.View3DViewFamilyTypeId ?? 0);
                    dataStorage.SetEntity(presetEntity);

                    transaction.Commit();
                }

                Logger.Info(
                    $"View preset saved: plan={options.CreatePlan}, " +
                    $"sections={options.CreateFrontView}/{options.CreateBackView}/{options.CreateRightView}/{options.CreateLeftView}, " +
                    $"3D={options.Create3D}, schedule={options.CreateSchedule}, use grouping={options.UseExistingGroupingParameter}.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not save view preset: {ex.Message}");
            }
        }

        private static int? ReadOptionalId(Entity entity, Schema schema, string fieldName)
        {
            int value = entity.Get<int>(schema.GetField(fieldName));
            return value > 0 ? value : (int?)null;
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
            builder.SetSchemaName("AssemblingManagerViewPreset");
            builder.AddSimpleField(UseGroupingField, typeof(bool));
            builder.AddSimpleField(CreatePlanField, typeof(bool));
            builder.AddSimpleField(CreateFrontViewField, typeof(bool));
            builder.AddSimpleField(CreateBackViewField, typeof(bool));
            builder.AddSimpleField(CreateRightViewField, typeof(bool));
            builder.AddSimpleField(CreateLeftViewField, typeof(bool));
            builder.AddSimpleField(Create3DField, typeof(bool));
            builder.AddSimpleField(CreateScheduleField, typeof(bool));
            builder.AddSimpleField(PlanTemplateIdField, typeof(int));
            builder.AddSimpleField(SectionTemplateIdField, typeof(int));
            builder.AddSimpleField(View3DTemplateIdField, typeof(int));
            builder.AddSimpleField(MasterScheduleIdField, typeof(int));
            builder.AddSimpleField(ScheduleViewTemplateIdField, typeof(int));
            builder.AddSimpleField(PlanViewFamilyTypeIdField, typeof(int));
            builder.AddSimpleField(SectionViewFamilyTypeIdField, typeof(int));
            builder.AddSimpleField(View3DViewFamilyTypeIdField, typeof(int));
            return builder.Finish();
        }
    }
}
