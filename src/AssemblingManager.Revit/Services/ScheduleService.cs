using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Services
{
    public class ScheduleService
    {
        public const string ScheduleSuffix = "_Спецификация";

        private static readonly HashSet<BuiltInCategory> ExcludedScheduleCategories =
            new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_Views,
                BuiltInCategory.OST_Sheets,
                BuiltInCategory.OST_Revisions,
                BuiltInCategory.OST_RevisionClouds,
                BuiltInCategory.OST_KeynoteTags
            };

        public List<ViewTemplateItem> GetAvailableScheduleItems(Document doc)
        {
            List<ViewTemplateItem> items = new List<ViewTemplateItem>
            {
                new ViewTemplateItem { Name = "— не выбрано —", Id = null }
            };

            List<ViewSchedule> schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => IsAvailableSchedule(s))
                .OrderBy(s => s.Name)
                .ToList();

            foreach (ViewSchedule schedule in schedules)
            {
#pragma warning disable CS0618
                int id = schedule.Id.IntegerValue;
#pragma warning restore CS0618

                items.Add(new ViewTemplateItem
                {
                    Name = schedule.Name,
                    Id = id
                });
            }

            return items;
        }

        private bool IsAvailableSchedule(ViewSchedule schedule)
        {
            if (schedule.IsTemplate)
            {
                return false;
            }

            if (schedule.IsTitleblockRevisionSchedule)
            {
                return false;
            }

            if (schedule.IsInternalKeynoteSchedule)
            {
                return false;
            }

            ElementId categoryId = schedule.Definition?.CategoryId;
            if (categoryId != null)
            {
#pragma warning disable CS0618
                BuiltInCategory category = (BuiltInCategory)categoryId.IntegerValue;
#pragma warning restore CS0618
                if (ExcludedScheduleCategories.Contains(category))
                {
                    return false;
                }
            }

            return true;
        }

        public List<ViewConflictItem> FindScheduleConflicts(Document doc, IEnumerable<AssemblyInstance> assemblies, ViewCreationOptions options)
        {
            List<ViewConflictItem> conflicts = new List<ViewConflictItem>();

            if (!options.CreateSchedule || !options.MasterScheduleId.HasValue)
            {
                return conflicts;
            }

            HashSet<string> existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name));

            foreach (AssemblyInstance assembly in assemblies)
            {
                string scheduleName = assembly.Name + ScheduleSuffix;
                if (existingNames.Contains(scheduleName))
                {
                    conflicts.Add(new ViewConflictItem
                    {
                        AssemblyName = assembly.Name,
                        ViewName = scheduleName,
                        ViewTypeDisplayName = "Спецификация",
                        ViewKind = ViewService.ViewKindSchedule,
                        Replace = false
                    });
                }
            }

            return conflicts;
        }

        public ViewSchedule DuplicateScheduleForAssembly(Document doc, ViewSchedule master, ElementId groupingParameterId, string assemblyName, int? scheduleTemplateId)
        {
            if (!master.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            {
                throw new InvalidOperationException($"Мастер-спецификация '{master.Name}' не может быть скопирована.");
            }

            ElementId newId = master.Duplicate(ViewDuplicateOption.Duplicate);
            ViewSchedule schedule = doc.GetElement(newId) as ViewSchedule;
            if (schedule == null)
            {
                throw new InvalidOperationException("Скопированная спецификация не является корректным объектом.");
            }

            schedule.Name = assemblyName + ScheduleSuffix;
            ScheduleDefinition definition = schedule.Definition;

            ScheduleField field = FindField(definition, groupingParameterId);
            if (field == null)
            {
                field = definition.AddField(ScheduleFieldType.Instance, groupingParameterId);
                if (field == null)
                {
                    throw new InvalidOperationException($"Не удалось добавить параметр группировки в спецификацию '{schedule.Name}'.");
                }

                field.IsHidden = true;
            }

            bool filterUpdated = false;
            for (int i = 0; i < definition.GetFilterCount(); i++)
            {
                ScheduleFilter filter = definition.GetFilter(i);
                if (filter.FieldId == field.FieldId)
                {
                    ScheduleFilter newFilter = new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, assemblyName);
                    definition.SetFilter(i, newFilter);
                    filterUpdated = true;
                    break;
                }
            }

            if (!filterUpdated)
            {
                ScheduleFilter newFilter = new ScheduleFilter(field.FieldId, ScheduleFilterType.Equal, assemblyName);
                definition.AddFilter(newFilter);
            }

            ApplyScheduleTemplate(schedule, scheduleTemplateId);

            Logger.Debug($"Created schedule '{schedule.Name}' for assembly '{assemblyName}'.");
            return schedule;
        }

        private ScheduleField FindField(ScheduleDefinition definition, ElementId parameterId)
        {
            foreach (ScheduleFieldId fieldId in definition.GetFieldOrder())
            {
                ScheduleField field = definition.GetField(fieldId);
                if (field != null && field.ParameterId == parameterId)
                {
                    return field;
                }
            }

            return null;
        }

        private void ApplyScheduleTemplate(ViewSchedule schedule, int? scheduleTemplateId)
        {
            if (!scheduleTemplateId.HasValue)
            {
                return;
            }

#pragma warning disable CS0618
            ElementId templateElementId = new ElementId(scheduleTemplateId.Value);
#pragma warning restore CS0618

            if (schedule.IsValidViewTemplate(templateElementId))
            {
                schedule.ViewTemplateId = templateElementId;
                Logger.Debug($"Applied schedule view template Id {scheduleTemplateId.Value} to schedule '{schedule.Name}'.");
            }
            else
            {
                Logger.Warn($"Schedule view template Id {scheduleTemplateId.Value} is not valid for schedule '{schedule.Name}'.");
            }
        }
    }
}
