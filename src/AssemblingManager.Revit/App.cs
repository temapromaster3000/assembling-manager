using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Common;

namespace AssemblingManager.Revit
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = Constants.PluginName;
                application.CreateRibbonTab(tabName);

                RibbonPanel assembliesPanel = application.CreateRibbonPanel(tabName, "Сборки");
                RibbonPanel sheetsPanel = application.CreateRibbonPanel(tabName, "Листы");

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData buttonData = new PushButtonData(
                    "CreateAssemblyViews",
                    "Сформировать\nвиды",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.CreateAssemblyViewsCommand");

                PushButton button = assembliesPanel.AddItem(buttonData) as PushButton;
                button.ToolTip = "Создать планы, разрезы и 3D виды для всех сборок в модели.";
                button.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Icon32.png");
                button.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Icon16.png");

                PushButtonData renameButtonData = new PushButtonData(
                    "RenameAssemblies",
                    "Переименовать\nвиды",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.RenameAssembliesCommand");

                PushButton renameButton = assembliesPanel.AddItem(renameButtonData) as PushButton;
                renameButton.ToolTip = "Найти переименованные сборки и привести имена видов, значение параметра и фильтры к текущему имени сборки.";
                renameButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Icon32.png");
                renameButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Icon16.png");

                PushButtonData placeViewsButtonData = new PushButtonData(
                    "PlaceViewsOnSheets",
                    "Разместить\nна листах",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.PlaceViewsOnSheetsCommand");

                PushButton placeViewsButton = sheetsPanel.AddItem(placeViewsButtonData) as PushButton;
                placeViewsButton.ToolTip = "Скопировать лист-образец для каждого объекта из выбранной группы видов и разместить виды вокруг листа.";
                placeViewsButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Icon32.png");
                placeViewsButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Icon16.png");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(Constants.PluginName, $"Ошибка при создании вкладки: {ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static BitmapImage LoadEmbeddedImage(string resourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
