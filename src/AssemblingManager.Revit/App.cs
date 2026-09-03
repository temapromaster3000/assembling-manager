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

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                RibbonPanel settingsPanel = application.CreateRibbonPanel(tabName, "Настройки");
                PushButtonData settingsButtonData = new PushButtonData(
                    "Settings",
                    "Настройки",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.SettingsCommand");

                PushButton settingsButton = settingsPanel.AddItem(settingsButtonData) as PushButton;
                settingsButton.ToolTip = "Версия плагина и проверка обновлений.";
                settingsButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Settings32.png");
                settingsButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Settings16.png");

                RibbonPanel assembliesPanel = application.CreateRibbonPanel(tabName, "Сборки");
                RibbonPanel sheetsPanel = application.CreateRibbonPanel(tabName, "Листы");
                RibbonPanel draftingPanel = application.CreateRibbonPanel(tabName, "Оформление");

                PushButtonData buttonData = new PushButtonData(
                    "CreateAssemblyViews",
                    "Сформировать\nвиды",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.CreateAssemblyViewsCommand");

                PushButton button = assembliesPanel.AddItem(buttonData) as PushButton;
                button.ToolTip = "Создать планы, разрезы и 3D виды для всех сборок в модели.";
                button.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.CreateViews32.png");
                button.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.CreateViews16.png");

                PushButtonData renameButtonData = new PushButtonData(
                    "RenameAssemblies",
                    "Переименовать\nвиды",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.RenameAssembliesCommand");

                PushButton renameButton = assembliesPanel.AddItem(renameButtonData) as PushButton;
                renameButton.ToolTip = "Найти переименованные сборки и привести имена видов, значение параметра и фильтры к текущему имени сборки.";
                renameButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Rename32.png");
                renameButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Rename16.png");

                PushButtonData placeViewsButtonData = new PushButtonData(
                    "PlaceViewsOnSheets",
                    "Разместить\nна листах",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.PlaceViewsOnSheetsCommand");

                PushButton placeViewsButton = sheetsPanel.AddItem(placeViewsButtonData) as PushButton;
                placeViewsButton.ToolTip = "Скопировать лист-образец для каждого объекта из выбранной группы видов и разместить виды вокруг листа.";
                placeViewsButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.PlaceSheets32.png");
                placeViewsButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.PlaceSheets16.png");

                PushButtonData sortSheetsButtonData = new PushButtonData(
                    "SortSheets",
                    "Сортировка листов",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.SortSheetsCommand");

                PushButton sortSheetsButton = sheetsPanel.AddItem(sortSheetsButtonData) as PushButton;
                sortSheetsButton.ToolTip = "Удалить пустые листы, привести имена листов к именам сборок и перенумеровать листы выбранной группы.";
                sortSheetsButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.SortSheets32.png");
                sortSheetsButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.SortSheets16.png");

                PushButtonData positionsButtonData = new PushButtonData(
                    "AssignAssemblyPositions",
                    "Проставить\nпозиции",
                    assemblyPath,
                    "AssemblingManager.Revit.Commands.AssignAssemblyPositionsCommand");

                PushButton positionsButton = draftingPanel.AddItem(positionsButtonData) as PushButton;
                positionsButton.ToolTip = "Проставить номера позиций по строкам выбранных спецификаций и пропустить строки по ключевым словам.";
                positionsButton.LargeImage = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Positions32.png");
                positionsButton.Image = LoadEmbeddedImage("AssemblingManager.Revit.Resources.Positions16.png");

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
