using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            int revitYear = ParseRevitYear(commandData.Application.Application.VersionNumber);
            SettingsDialog dialog = new SettingsDialog(revitYear);
            dialog.ShowDialog();
            return Result.Succeeded;
        }

        private static int ParseRevitYear(string versionNumber)
        {
            int year;
            if (int.TryParse(versionNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out year) &&
                year >= 2000 && year < 2100)
            {
                return year;
            }
            return 0;
        }
    }
}
