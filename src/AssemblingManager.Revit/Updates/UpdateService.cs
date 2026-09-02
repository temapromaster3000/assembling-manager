using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssemblingManager.Core.Utils;

namespace AssemblingManager.Revit.Updates
{
    public class ReleaseAsset
    {
        public string Tag { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
    }

    public class ReleaseInfo
    {
        public string Version { get; set; }
        public string TagName { get; set; }
        public string ReleaseNotes { get; set; }
        public List<ReleaseAsset> Assets { get; set; }
    }

    public class UpdateService
    {
        private const string GitHubOwner = "temapromaster3000";
        private const string GitHubRepo = "assembling-manager";
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases?per_page=20";

        private const string AssetNamePattern = "^AssemblingManager-R([0-9]{2})\\.zip$";
        private const string UpdaterFileName = "AssemblingManager.Updater.exe";
        private const string PluginDllName = "AssemblingManager.dll";
        private const string MarkerFileName = "update-pending.txt";

        private static readonly int[] SupportedYears = { 2021, 2022, 2023, 2024, 2025 };

        private static readonly string RootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssemblingManager");
        private static readonly string StagingDir = Path.Combine(RootDir, "staging");
        private static readonly string MarkerPath = Path.Combine(RootDir, MarkerFileName);

        private static readonly Regex AssetNameRegex =
            new Regex(AssetNamePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly HttpClient _httpClient;

        public UpdateService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AssemblingManager-UpdateService");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public string GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyInformationalVersionAttribute informational =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
            {
                return informational.InformationalVersion.Split('+')[0].Trim();
            }

            Version assemblyVersion = assembly.GetName().Version;
            return assemblyVersion == null ? "0.0.0" : string.Format("{0}.{1}.{2}", assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }

        public async Task<ReleaseInfo> CheckForUpdateAsync(int revitYear)
        {
            string currentVersion = GetCurrentVersion();

            using (HttpResponseMessage response = await _httpClient.GetAsync(ReleasesApiUrl).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw BuildApiException(response);
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    return ParseLatestStableRelease(document.RootElement, currentVersion, revitYear);
                }
            }
        }

        public async Task DownloadAndStageAsync(ReleaseInfo release, int revitYear, IProgress<double> progress)
        {
            List<int> years = CollectTargetYears(revitYear);
            List<ReleaseAsset> downloads = new List<ReleaseAsset>();
            foreach (string tag in years.Select(YearToTag).Distinct())
            {
                ReleaseAsset asset = release.Assets.FirstOrDefault(a =>
                    string.Equals(a.Tag, tag, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    throw new InvalidOperationException(
                        "В релизе v" + release.Version + " нет файла обновления для Revit " +
                        TagToYearLabel(tag) + " (" + tag + ").");
                }
                downloads.Add(asset);
            }

            if (Directory.Exists(StagingDir))
            {
                try
                {
                    Directory.Delete(StagingDir, true);
                }
                catch (IOException)
                {
                }
            }
            Directory.CreateDirectory(StagingDir);

            long totalBytes = downloads.Sum(a => Math.Max(a.Size, 0));
            long downloadedBytes = 0;
            byte[] buffer = new byte[81920];

            foreach (ReleaseAsset asset in downloads)
            {
                string zipPath = Path.Combine(StagingDir, asset.Tag + ".zip");
                using (HttpResponseMessage fileResponse =
                       await _httpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    fileResponse.EnsureSuccessStatusCode();

                    using (Stream contentStream = await fileResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream fileStream = File.Create(zipPath))
                    {
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                            downloadedBytes += read;
                            if (progress != null && totalBytes > 0)
                            {
                                progress.Report((double)downloadedBytes / totalBytes);
                            }
                        }
                    }
                }

                string extractDir = Path.Combine(StagingDir, asset.Tag);
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, true);
                }
                ZipFile.ExtractToDirectory(zipPath, extractDir);
                File.Delete(zipPath);
            }

            WriteMarker(release, years);
        }

        public string FindUpdaterExe()
        {
            string installedPath = Path.Combine(RootDir, UpdaterFileName);
            if (File.Exists(installedPath))
            {
                return installedPath;
            }

            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string binDir = Path.GetDirectoryName(pluginDir.TrimEnd(Path.DirectorySeparatorChar));
                if (binDir != null)
                {
                    string buildOutputPath = Path.Combine(binDir, "Updater", UpdaterFileName);
                    if (File.Exists(buildOutputPath))
                    {
                        return buildOutputPath;
                    }
                }
            }
            catch (ArgumentException)
            {
            }

            return null;
        }

        public void LaunchUpdater()
        {
            string updaterPath = FindUpdaterExe();
            if (updaterPath == null)
            {
                throw new InvalidOperationException(
                    "Не найден файл " + UpdaterFileName + ". Переустановите плагин установщиком.");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(startInfo);
        }

        private ReleaseInfo ParseLatestStableRelease(JsonElement releases, string currentVersion, int revitYear)
        {
            JsonElement latest = default;
            string latestVersion = null;

            foreach (JsonElement release in releases.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean())
                {
                    continue;
                }

                if (release.TryGetProperty("prerelease", out JsonElement prerelease) && prerelease.GetBoolean())
                {
                    continue;
                }

                string tagName = release.TryGetProperty("tag_name", out JsonElement tagElement)
                    ? tagElement.GetString()
                    : null;
                string version = (tagName ?? string.Empty).TrimStart('v', 'V');

                if (PluginVersion.Compare(version, currentVersion) <= 0)
                {
                    continue;
                }

                if (latestVersion == null || PluginVersion.Compare(version, latestVersion) > 0)
                {
                    latestVersion = version;
                    latest = release;
                }
            }

            if (latestVersion == null)
            {
                return null;
            }

            List<ReleaseAsset> assets = new List<ReleaseAsset>();
            if (latest.TryGetProperty("assets", out JsonElement assetsElement))
            {
                foreach (JsonElement assetElement in assetsElement.EnumerateArray())
                {
                    string name = assetElement.TryGetProperty("name", out JsonElement nameElement)
                        ? nameElement.GetString()
                        : null;
                    Match match = name == null ? null : AssetNameRegex.Match(name);
                    if (match == null || !match.Success)
                    {
                        continue;
                    }

                    string url = assetElement.TryGetProperty("browser_download_url", out JsonElement urlElement)
                        ? urlElement.GetString()
                        : null;
                    if (string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    assets.Add(new ReleaseAsset
                    {
                        Tag = "R" + match.Groups[1].Value,
                        Url = url,
                        Size = assetElement.TryGetProperty("size", out JsonElement sizeElement) ? sizeElement.GetInt64() : 0
                    });
                }
            }

            string body = latest.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.GetString() : null;

            return new ReleaseInfo
            {
                Version = latestVersion,
                TagName = latest.TryGetProperty("tag_name", out JsonElement latestTag) ? latestTag.GetString() : "v" + latestVersion,
                ReleaseNotes = string.IsNullOrWhiteSpace(body) ? "(описание релиза отсутствует)" : body.Trim(),
                Assets = assets
            };
        }

        private InvalidOperationException BuildApiException(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string> remaining) &&
                    remaining.FirstOrDefault() == "0" &&
                    response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string> resets) &&
                    long.TryParse(resets.FirstOrDefault(), out long resetUnix))
                {
                    DateTimeOffset resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix).LocalDateTime;
                    return new InvalidOperationException(
                        "Превышен лимит обращений к GitHub. Повторите после " +
                        resetTime.ToString("HH:mm") + ".");
                }

                return new InvalidOperationException(
                    "GitHub отклонил запрос (403). Попробуйте позже.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new InvalidOperationException(
                    "Релизы не найдены: " + GitHubOwner + "/" + GitHubRepo + ".");
            }

            return new InvalidOperationException(
                "GitHub вернул ошибку " + (int)response.StatusCode + " " + response.StatusCode + ".");
        }

        private List<int> CollectTargetYears(int revitYear)
        {
            List<int> years = new List<int>();
            if (SupportedYears.Contains(revitYear))
            {
                years.Add(revitYear);
            }

            try
            {
                if (Directory.Exists(RootDir))
                {
                    foreach (string dir in Directory.GetDirectories(RootDir))
                    {
                        string name = Path.GetFileName(dir);
                        int year;
                        if (name.Length == 4 && int.TryParse(name, out year) &&
                            SupportedYears.Contains(year) &&
                            File.Exists(Path.Combine(dir, PluginDllName)) &&
                            !years.Contains(year))
                        {
                            years.Add(year);
                        }
                    }
                }
            }
            catch (IOException)
            {
            }

            if (!years.Contains(revitYear))
            {
                try
                {
                    string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string currentName = Path.GetFileName(currentDir.TrimEnd(Path.DirectorySeparatorChar));
                    int currentYear;
                    if (currentName.Length == 4 && int.TryParse(currentName, out currentYear) &&
                        SupportedYears.Contains(currentYear))
                    {
                        years.Add(currentYear);
                    }
                    else if (revitYear == 0)
                    {
                        throw new InvalidOperationException(
                            "Не удалось определить версию Revit для загрузки обновления.");
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            if (years.Count == 0)
            {
                throw new InvalidOperationException(
                    "Не удалось определить ни одной установленной версии плагина для обновления.");
            }

            return years.OrderBy(y => y).ToList();
        }

        private static string YearToTag(int year)
        {
            return "R" + (year % 100).ToString("00");
        }

        private static string TagToYearLabel(string tag)
        {
            int number;
            if (tag != null && tag.Length == 3 && tag[0] == 'R' && int.TryParse(tag.Substring(1), out number))
            {
                int year = 2000 + number;
                if (year == 2021 || year == 2022 || year == 2023)
                {
                    return "2021-2023";
                }
                return year.ToString();
            }
            return tag;
        }

        private void WriteMarker(ReleaseInfo release, List<int> years)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# AssemblingManager pending update");
            builder.AppendLine("Version=" + release.Version);

            foreach (int year in years)
            {
                string tag = YearToTag(year);
                string staging = Path.Combine(StagingDir, tag);
                if (!Directory.Exists(staging))
                {
                    continue;
                }

                builder.AppendLine("[Artifact]");
                builder.AppendLine("StagingDir=" + staging);
                builder.AppendLine("TargetDir=" + GetTargetDir(year));
            }

            Directory.CreateDirectory(RootDir);
            File.WriteAllText(MarkerPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static string GetTargetDir(int year)
        {
            string appDataYearDir = Path.Combine(RootDir, year.ToString());
            if (Directory.Exists(appDataYearDir))
            {
                return appDataYearDir;
            }

            try
            {
                string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string currentName = Path.GetFileName(currentDir.TrimEnd(Path.DirectorySeparatorChar));
                if (string.Equals(currentName, year.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return currentDir;
                }
            }
            catch (ArgumentException)
            {
            }

            return appDataYearDir;
        }
    }
}
