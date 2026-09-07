using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblingManager.Revit.Services
{
    public class FailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                try
                {
                    string elementIds = string.Join(", ", failure.GetFailingElementIds().Select(id => id.ToString()));
                    Logger.Warn($"Revit failure [{failure.GetSeverity()}]: {failure.GetDescriptionText()} (elements: {elementIds})");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not log a Revit failure: {ex.Message}");
                }

                try
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Could not delete failure as warning: {ex.Message}");
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
