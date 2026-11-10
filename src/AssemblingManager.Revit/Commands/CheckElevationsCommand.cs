using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Common;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CheckElevationsCommand : IExternalCommand
    {
        private static ElevationCheckDialog _activeDialog;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument uiDocument = uiApplication.ActiveUIDocument;
            Document document = uiDocument?.Document;

            if (document == null)
            {
                TaskDialog.Show(Constants.PluginName, "Необходимо открыть модель.");
                return Result.Cancelled;
            }

            if (_activeDialog != null && _activeDialog.IsVisible)
            {
                _activeDialog.Activate();
                return Result.Succeeded;
            }

            Logger.Info("=== CheckElevationsCommand started ===");
            Logger.Info($"Document: {document.Title}");

            List<SheetGroupNode> sheetRoots;

            try
            {
                BrowserGroupService browserGroupService = new BrowserGroupService();
                sheetRoots = Logger.Time(
                    "CheckElevations.BuildSheetTree",
                    () => browserGroupService.BuildSheetTree(document));
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to build sheet tree: {ex}");
                TaskDialog.Show(
                    Constants.PluginName,
                    $"Не удалось прочитать структуру диспетчера листов: {ex.Message}");
                return Result.Failed;
            }

            int totalSheets = sheetRoots.Sum(root => root.CountSheets());
            if (totalSheets == 0)
            {
                Logger.Warn("No sheets found. Command cancelled.");
                TaskDialog.Show(Constants.PluginName, "В модели нет листов.");
                return Result.Cancelled;
            }

            List<ElevationSheetTreeItem> tree = ElevationSheetTreeItem.Build(sheetRoots);
            ElevationCheckEventHandler handler = new ElevationCheckEventHandler(document);
            ExternalEvent externalEvent = null;
            ElevationCheckDialog dialog = null;

            try
            {
                externalEvent = ExternalEvent.Create(handler);
                dialog = new ElevationCheckDialog(uiApplication, document, tree, handler, externalEvent);
                _activeDialog = dialog;

                dialog.Closed += (sender, args) =>
                {
                    if (ReferenceEquals(_activeDialog, dialog))
                    {
                        _activeDialog = null;
                    }
                };

                dialog.Show();

                Logger.Info($"CheckElevations window shown: {totalSheets} sheets available.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to show CheckElevations window: {ex}");
                _activeDialog = null;

                if (dialog != null)
                {
                    try
                    {
                        dialog.Close();
                    }
                    catch (Exception closeEx)
                    {
                        Logger.Warn($"Could not close failed CheckElevations window: {closeEx.Message}");
                    }
                }

                try
                {
                    externalEvent?.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Logger.Warn($"Could not dispose CheckElevations external event: {disposeEx.Message}");
                }

                TaskDialog.Show(
                    Constants.PluginName,
                    $"Не удалось открыть окно проверки отметок: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Закрывает окно модуля при выключении Revit.
        /// </summary>
        public static void CloseActiveDialog()
        {
            ElevationCheckDialog dialog = _activeDialog;
            _activeDialog = null;

            if (dialog == null)
            {
                return;
            }

            try
            {
                if (dialog.IsVisible)
                {
                    dialog.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not close CheckElevations window on shutdown: {ex.Message}");
            }
        }
    }
}
