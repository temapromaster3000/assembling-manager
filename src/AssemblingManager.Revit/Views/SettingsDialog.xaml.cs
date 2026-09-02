using System;
using System.Windows;
using AssemblingManager.Core.Common;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Updates;

namespace AssemblingManager.Revit.Views
{
    public partial class SettingsDialog : Window
    {
        private readonly int _revitYear;
        private readonly UpdateService _updateService = new UpdateService();
        private ReleaseInfo _pendingRelease;

        public SettingsDialog(int revitYear)
        {
            InitializeComponent();
            _revitYear = revitYear;

            string versionText = "Версия плагина: v" + _updateService.GetCurrentVersion();
            if (revitYear > 0)
            {
                versionText += "   |   Revit " + revitYear;
            }
            VersionText.Text = versionText;
        }

        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingRelease = null;
            DownloadButton.Visibility = Visibility.Collapsed;
            NotesHeader.Visibility = Visibility.Collapsed;
            NotesScroll.Visibility = Visibility.Collapsed;
            StatusText.Text = "Проверяем обновления...";

            CheckButton.IsEnabled = false;
            DownloadProgress.IsIndeterminate = true;
            DownloadProgress.Visibility = Visibility.Visible;

            try
            {
                ReleaseInfo release = await _updateService.CheckForUpdateAsync(_revitYear);

                if (release == null)
                {
                    StatusText.Text = "У вас установлена последняя версия.";
                }
                else
                {
                    _pendingRelease = release;
                    StatusText.Text = "Доступна новая версия: v" + release.Version + ".";
                    NotesHeader.Visibility = Visibility.Visible;
                    NotesText.Text = release.ReleaseNotes;
                    NotesScroll.Visibility = Visibility.Visible;
                    DownloadButton.Content = "Скачать и установить v" + release.Version;
                    DownloadButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Update check failed: " + ex);
                StatusText.Text = "Не удалось проверить обновления: " + ex.Message;
            }
            finally
            {
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Visibility = Visibility.Collapsed;
                CheckButton.IsEnabled = true;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingRelease == null)
            {
                return;
            }

            CheckButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            DownloadButton.Content = "Скачивание...";

            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Value = 0;
            DownloadProgress.Visibility = Visibility.Visible;

            try
            {
                Progress<double> progress = new Progress<double>(value => DownloadProgress.Value = value);
                await _updateService.DownloadAndStageAsync(_pendingRelease, _revitYear, progress);
                _updateService.LaunchUpdater();

                StatusText.Text = "Обновление v" + _pendingRelease.Version + " скачано. " +
                                  "Закройте Revit — оно применится автоматически при выходе.";
                DownloadButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Error("Update download failed: " + ex);
                StatusText.Text = "Не удалось скачать обновление: " + ex.Message;
                DownloadButton.Content = "Скачать и установить v" + _pendingRelease.Version;
            }
            finally
            {
                DownloadProgress.Visibility = Visibility.Collapsed;
                CheckButton.IsEnabled = true;
                DownloadButton.IsEnabled = true;
            }
        }
    }
}
