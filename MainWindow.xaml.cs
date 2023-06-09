﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Resources;

namespace ChronicleLauncher
{
    public partial class MainWindow : Window
    {
        private struct SLatestVersionData
        {
            public SLatestVersionData() { }
            public string s_zipUrl = string.Empty;
            public string s_unzipName = string.Empty;
            public string s_versionName = string.Empty;
        }

        // Update this if you release a new version
        // OLD VERSIONS
        // "Launcher Alpha 1.0.0"
        private static readonly string CURRENT_VERSION = "Launcher Alpha 1.0.1";

        private static readonly CancellationTokenSource cancellationTokenSource = new();

        static bool isDownloading = false;
        static readonly string chronicleBaseUrl = "http://www.chroniclerewritten.com/api/";
        private static string m_latestExecutibleLocation = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            VersionText.Text = CURRENT_VERSION;

            StreamResourceInfo streamResource = Application.GetResourceStream(new Uri("Images/Ready_Icons/Sprite_UI_Cursor_Normal_64_new.cur", UriKind.Relative));
            Cursor = new Cursor(streamResource.Stream);

            var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            // Initilise our exit route for download cancellation
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
        }

        // Window methods
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private async void Window_ContentRendered(object sender, EventArgs e)
        {
            SLatestVersionData latestLauncherVersionData = await GetLatestLauncherVersionAsync();

            if (latestLauncherVersionData.s_versionName != string.Empty &&
               latestLauncherVersionData.s_versionName != CURRENT_VERSION)
            {
                MessageBoxResult result = MessageBox.Show("Do you want to download the latest launcher version to your downloads folder: " + latestLauncherVersionData.s_versionName + " ?",
                                                            "New version available!",
                                                            MessageBoxButton.YesNo,
                                                            MessageBoxImage.Information,
                                                            MessageBoxResult.Yes);

                if (result == MessageBoxResult.Yes)
                {

                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = "http://www.chroniclerewritten.com/",
                        UseShellExecute = true
                    });
                    Close();
                }
            }

            SLatestVersionData latestVersionData = await GetLatestGameVersionAsync();

            HttpClient downloadClient = new();

            string downloadLink = "https://www.chroniclerewritten.com/" + latestVersionData.s_zipUrl;
            string latestVersionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version", latestVersionData.s_unzipName);

            if (!Directory.Exists(latestVersionPath))
            {
                downloadClient.BaseAddress = new Uri(chronicleBaseUrl);
                downloadClient.DefaultRequestHeaders.Accept.Clear();
                downloadClient.Timeout = TimeSpan.FromMinutes(60);

                var progressIndicator = new Progress<float>(ReportProgress);

                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                Directory.CreateDirectory(tempDir);
                string downloadingFile = Path.Combine(tempDir, latestVersionData.s_versionName + ".zip");

                Download_Progress.Maximum = await GetFileSize(downloadLink);
                if (File.Exists(downloadingFile))
                {
                    FileInfo fi = new(downloadingFile);
                    if (fi.Length != Download_Progress.Maximum)
                    {
                        File.Delete(downloadingFile);
                    }
                }

                if (!File.Exists(downloadingFile))
                {
                    using var file = new FileStream(downloadingFile, FileMode.Create, FileAccess.Write, FileShare.None);
                    Play_Button_Text.Text = " Downloading Version " + latestVersionData.s_versionName;

                    isDownloading = true;

                    try
                    {
                        await downloadClient.DownloadAsync(downloadLink, file, progressIndicator, cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException exception)
                    {
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            downloadClient.Dispose();
                            file.Dispose();

                            if (Directory.Exists(tempDir))
                            {
                                Directory.Delete(tempDir, true);
                            }

                            Close();
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Download failed due to unexpected error: " + exception);
                        }
                    }

                    isDownloading = false;
                }
                downloadClient.Dispose();

                // remove all older versions
                if (Directory.Exists(AppDomain.CurrentDomain.BaseDirectory + "\\version\\"))
                {
                    Directory.Delete(AppDomain.CurrentDomain.BaseDirectory + "\\version\\", true);
                }

                string tempPath = Path.Combine(tempDir, latestVersionData.s_versionName + ".zip");
                Download_Progress_Label.Text = " Extracting Files";
                System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version"));
            }

            //Update the UI to reflect the progress value that is passed back.
            Download_Progress_Label.Text = " Chronicle Version is up to date!";
            Play_Button_Text.Text = " Play! ";
            Download_Progress.Value = Download_Progress.Maximum;
            Ready_Icon_Success.Visibility = Visibility.Visible;
            Ready_Icon_Failure.Visibility = Visibility.Collapsed;
            Ready_Icon_Failure_Shine.Visibility = Visibility.Collapsed;

            m_latestExecutibleLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version", latestVersionData.s_unzipName);
        }

        // Click methods
        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(m_latestExecutibleLocation))
                return;

            Play_Button_Text.Text = " Launching Chronicle... ";

            try
            {
                // Copy any prexisting decks and settings to chronicle
                if (Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings")))
                {
                    var sourceDir = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings"));
                    sourceDir.DeepCopy(Path.Combine(m_latestExecutibleLocation, "Settings"));
                }
                if (Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Decks")))
                {
                    var sourceDir = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Decks"));
                    sourceDir.DeepCopy(Path.Combine(m_latestExecutibleLocation, "Decks"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Problem copying data from Launcher to Chronicle:" + ex.ToString());
            }

            StartChronicle(m_latestExecutibleLocation).Wait();

            try
            {
                // copy decks and settings to from chronicle version to launcher instance if they exist
                if (Directory.Exists(Path.Combine(m_latestExecutibleLocation, "Settings")))
                {
                    Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings"));
                    var sourceDir = new DirectoryInfo(Path.Combine(m_latestExecutibleLocation, "Settings"));
                    sourceDir.DeepCopy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings"));
                }
                if (Directory.Exists(Path.Combine(m_latestExecutibleLocation, "Decks")))
                {
                    Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Decks"));
                    var sourceDir = new DirectoryInfo(Path.Combine(m_latestExecutibleLocation, "Decks"));
                    sourceDir.DeepCopy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Decks"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Problem copying data from Chronicle to Launcher:" + ex.ToString());
            }

            Play_Button_Text.Text = " Play! ";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (isDownloading)
            {
                cancellationTokenSource.Cancel();
            }
            else
            {
                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }

                Close();
            }
        }
        private void MinimiseButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
        }

        // Functional Methods
        void ReportProgress(float value)
        {
            //Update the UI to reflect the progress value that is passed back.
            Download_Progress_Label.Text = " " + Math.Truncate(((value / Download_Progress.Maximum) * 100)).ToString() + "%";
            Download_Progress.Value = value;
        }

        private static async Task<SLatestVersionData> GetLatestGameVersionAsync()
        {
            SLatestVersionData returnData = new();

            HttpClient versionClient = new()
            {
                BaseAddress = new Uri(chronicleBaseUrl)
            };
            versionClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));

            var requestData = new { route = "getgameversions" };
            var jsonContent = JsonSerializer.Serialize(requestData);
            var requestContent = new StringContent($"data={Uri.EscapeDataString(jsonContent)}", Encoding.UTF8, "application/x-www-form-urlencoded");

            HttpResponseMessage response = await versionClient.PostAsync("", requestContent);

            if (response.IsSuccessStatusCode)
            {
                string jsonString = await response.Content.ReadAsStringAsync();

                JsonNode? jo = JsonNode.Parse(jsonString);
                if (jo != null)
                {
                    JsonNode? gameVersions = jo["game_versions"];
                    if (gameVersions != null)
                    {
                        JsonArray gameVersionArray = gameVersions.AsArray();
                        if (gameVersionArray != null)
                        {
                            JsonNode? latestGameVersion = gameVersionArray.LastOrDefault();
                            if (latestGameVersion != null)
                            {
                                JsonNode? urlNode = latestGameVersion["url"];
                                if (urlNode != null)
                                {
                                    returnData.s_zipUrl = urlNode.GetValue<string>();
                                    int pFrom = returnData.s_zipUrl.LastIndexOf("/") + 1;
                                    int pTo = returnData.s_zipUrl.LastIndexOf(".");
                                    returnData.s_unzipName = returnData.s_zipUrl[pFrom..pTo];
                                }

                                JsonNode? nameNode = latestGameVersion["name"];
                                if (nameNode != null)
                                {
                                    returnData.s_versionName = nameNode.GetValue<string>();
                                }
                            }
                        }
                    }
                }
            }
            versionClient.Dispose();
            return returnData;
        }

        public static async Task<long> GetFileSize(string url)
        {
            try
            {
                using HttpClient httpClient = new();
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/88.0.4324.150 Safari/537.36");

                HttpResponseMessage response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentLength != null)
                    {
                        return (long)response.Content.Headers.ContentLength;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while retrieving the file size: {ex.Message}");
            }

            return -1; // Return -1 if the file size couldn't be retrieved
        }

        private async Task StartChronicle(string latestExecutableLocation)
        {
            // Use ProcessStartInfo class
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = latestExecutableLocation,
                CreateNoWindow = true,
                UseShellExecute = true,
                FileName = Path.Combine(latestExecutableLocation, "Chronicle.exe")
            };

            try
            {
                // Start the process with the info we specified.
                // Call WaitForExit and then the using statement will close.
                if (startInfo != null)
                {
                    var exeProcess = Process.Start(startInfo)
                        ?? throw new ArgumentException(latestExecutableLocation);

                    Play_Button_Text.Text = " Chronicle Launched ";
                    await exeProcess.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Problem launching Chronicle: " + ex.ToString());
            }
        }

        private static async Task<SLatestVersionData> GetLatestLauncherVersionAsync()
        {
            SLatestVersionData returnData = new();
            HttpClient versionClient = new();
            try
            {
                versionClient.BaseAddress = new Uri(chronicleBaseUrl);
                versionClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));

                var requestData = new { route = "getlauncherversions" };
                var jsonContent = JsonSerializer.Serialize(requestData);
                var requestContent = new StringContent($"data={Uri.EscapeDataString(jsonContent)}", Encoding.UTF8, "application/x-www-form-urlencoded");
                
                HttpResponseMessage response = await versionClient.PostAsync("", requestContent);

                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();

                    JsonNode? jo = JsonNode.Parse(jsonString);
                    if (jo != null)
                    {
                        JsonNode? gameVersions = jo["launcher_versions"];
                        if (gameVersions != null)
                        {
                            JsonArray gameVersionArray = gameVersions.AsArray();
                            if (gameVersionArray != null)
                            {
                                JsonNode? latestGameVersion = gameVersionArray.LastOrDefault();
                                if (latestGameVersion != null)
                                {
                                    JsonNode? urlNode = latestGameVersion["url"];
                                    if (urlNode != null)
                                    {
                                        returnData.s_zipUrl = urlNode.GetValue<string>();
                                        int pFrom = returnData.s_zipUrl.LastIndexOf("/") + 1;
                                        returnData.s_unzipName = returnData.s_zipUrl[pFrom..returnData.s_zipUrl.Length];
                                    }

                                    JsonNode? nameNode = latestGameVersion["name"];
                                    if (nameNode != null)
                                    {
                                        returnData.s_versionName = nameNode.GetValue<string>();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while retrieving the file size: {ex.Message}");
            }
            versionClient.Dispose();

            return returnData;
        }
    }
}
