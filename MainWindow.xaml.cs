using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ChronicleLauncher
{
    public partial class MainWindow : Window
    {
        private struct SVersionData
        {
            public SVersionData() { }
            public string s_versionString = string.Empty;
            public string s_zipName = string.Empty;
            public string s_unzipName = string.Empty;
        }
        /*
        static SLatestVersionData currentlatestVersionData;*/

        // Update this if you release a new version
        // OLD VERSIONS
        // "Launcher Alpha 1.0.0"
        // "Launcher Alpha 1.0.1"
        // "Launcher Alpha 1.0.2"
        // "Launcher Alpha 1.0.3"
        private static readonly string CURRENT_VERSION = "Launcher Alpha 1.0.5";

        private static CancellationTokenSource cancellationTokenSource = new();
        static private string m_versionID = string.Empty;
        static private string m_versionString = string.Empty;

        static bool isDownloading = false;
        static bool readyToDownload = false;
        static bool readyToPlay = false;
        static string chronicleBaseUrl = "http://www.chroniclerewritten.com/";
        static string chronicleApiUrl = "http://www.chroniclerewritten.com/api/";
        private static string m_latestExecutibleLocation = string.Empty;

        private LauncherSettings settingsManager;
        private Settings currentLauncherSettings;
        public MainWindow()
        {
            InitializeComponent();

            VersionText.Text = CURRENT_VERSION;

            var settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcherSettings.json");
            settingsManager = new(settingsFilePath);
            if (File.Exists(settingsFilePath))
            {
                currentLauncherSettings = settingsManager.LoadSettings();
            }
            else
            {
                currentLauncherSettings = new Settings();
                currentLauncherSettings.IsTesting = false;
                settingsManager.SaveSettings(currentLauncherSettings);
            }

            Cursor = new Cursor(Application.GetResourceStream(new Uri("Images/Ready_Icons/Sprite_UI_Cursor_Normal_64_new.cur", UriKind.Relative)).Stream);

            // Initilise our exit route for download cancellation
            cancellationTokenSource.Token.ThrowIfCancellationRequested();

            TestToggle();

            var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            var tempTestingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tempTESTING");
            if (Directory.Exists(tempTestingDir))
            {
                Directory.Delete(tempTestingDir, true);
            }
        }

        // Window methods
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        public void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                settingsManager.SaveSettings(currentLauncherSettings);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async void Window_ContentRendered(object sender, EventArgs e)
        {
            SVersionData latestLauncherVersionData = await GetLatestLauncherVersionAsync();

            if (latestLauncherVersionData.s_versionString != string.Empty &&
                latestLauncherVersionData.s_versionString != CURRENT_VERSION)
            {
                MessageBoxResult result = MessageBox.Show("Do you want to download the latest launcher version? : " + latestLauncherVersionData.s_versionString + " ?",
                                                            "New version available!",
                                                            MessageBoxButton.YesNo,
                                                            MessageBoxImage.Information,
                                                            MessageBoxResult.Yes);

                if (result == MessageBoxResult.Yes)
                {

                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = chronicleBaseUrl,
                        UseShellExecute = true
                    });
                    Close();
                }
            }

            CheckGameVersionAsync();
        }

        private async void CheckGameVersionAsync()
        {
            SVersionData gameVersionData = await GetLatestGameVersionAsync();
            string latestVersionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, (currentLauncherSettings.IsTesting ? "versionTESTING" : "version"), gameVersionData.s_unzipName);

            if (!Directory.Exists(latestVersionPath))
            {
                Download_Progress_Label.Text = " Click to download chronicle version:" + gameVersionData.s_versionString;
                Play_Button_Text.Text = " New version available! ";
                Download_Progress.Value = Download_Progress.Minimum;
                Ready_Icon_Success.Visibility = Visibility.Collapsed;
                Ready_Icon_Failure.Visibility = Visibility.Visible;
                Play_Success_Glow.Visibility = Visibility.Collapsed;

                readyToDownload = true;
            }
            else 
            { 
                //Update the UI to reflect the progress value that is passed back.
                Download_Progress_Label.Text = " Chronicle Version is up to date!";
                Play_Button_Text.Text = " Play! ";
                Download_Progress.Value = Download_Progress.Maximum;
                Ready_Icon_Success.Visibility = Visibility.Visible;
                Ready_Icon_Failure.Visibility = Visibility.Collapsed;

                readyToDownload = false;
                readyToPlay = true;
                Play_Success_Glow.Visibility = Visibility.Visible;

                m_latestExecutibleLocation = latestVersionPath;
            }
        }

        private async void DownloadGameAsync()
        {
            readyToDownload = false;
            HttpClient downloadClient = new();

            SVersionData gameData = await GetGameUrlAsync(m_versionID);
            string downloadLink = chronicleBaseUrl + gameData.s_zipName;

            downloadClient.BaseAddress = new Uri(chronicleApiUrl);
            downloadClient.DefaultRequestHeaders.Accept.Clear();
            downloadClient.Timeout = TimeSpan.FromMinutes(60);

            var progressIndicator = new Progress<float>(ReportProgress);

            string tempDir = AppDomain.CurrentDomain.BaseDirectory + (currentLauncherSettings.IsTesting ? "\\tempTESTING\\" : "\\temp\\");
            Directory.CreateDirectory(tempDir);
            string downloadingFile = Path.Combine(tempDir, m_versionString + ".zip");

            Download_Progress.Maximum = await GetFileSizeAsync(downloadLink);
            if (File.Exists(downloadingFile) && new FileInfo(downloadingFile).Length != Download_Progress.Maximum)
            {
                File.Delete(downloadingFile);
            }

            if (!File.Exists(downloadingFile))
            {
                using var file = new FileStream(downloadingFile, FileMode.Create, FileAccess.Write, FileShare.None);
                Play_Button_Text.Text = " Downloading Version " + m_versionString;

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
            if (Directory.Exists(AppDomain.CurrentDomain.BaseDirectory + (currentLauncherSettings.IsTesting ? "\\versionTESTING\\" : "\\version\\")))
            {
                Directory.Delete(AppDomain.CurrentDomain.BaseDirectory + (currentLauncherSettings.IsTesting ? "\\versionTESTING\\" : "\\version\\"), true);
            }
            var extractionLocation = AppDomain.CurrentDomain.BaseDirectory + (currentLauncherSettings.IsTesting ? "versionTESTING\\" : "version\\");
            Directory.CreateDirectory(extractionLocation);

            string tempPath = Path.Combine(tempDir, m_versionString + ".zip");
            Download_Progress_Label.Text = " Extracting Files";
            System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, extractionLocation);

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            m_latestExecutibleLocation = AppDomain.CurrentDomain.BaseDirectory + (currentLauncherSettings.IsTesting ? "versionTESTING\\" : "version\\") + gameData.s_unzipName;

            CheckGameVersionAsync();
        }

        private async void PlayButtonWrapper()
        {
            if (string.IsNullOrEmpty(m_latestExecutibleLocation))
                return;

            Play_Button_Text.Text = " Launching Chronicle... ";
            Ready_Icon_Success_Clicked.Visibility = Visibility.Visible;
            Play_Success_Glow.Visibility = Visibility.Collapsed;

            await Task.Run(() =>
            {
                try
                {
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
                    Console.WriteLine("Problem copying data from Launcher to Chronicle: " + ex.ToString());
                }
            });

            await StartChronicleAsync(m_latestExecutibleLocation);

            await Task.Run(() =>
            {
                try
                {
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
                    Console.WriteLine("Problem copying data from Chronicle to Launcher: " + ex.ToString());
                }
            });

            Ready_Icon_Success_Clicked.Visibility = Visibility.Collapsed;
            Play_Success_Glow.Visibility = Visibility.Visible;
            Play_Button_Text.Text = " Play! ";
        }

        private void TestToggle()
        {
            if (currentLauncherSettings.IsTesting)
            {
                chronicleBaseUrl = "http://www.testing.chroniclerewritten.com/";
                chronicleApiUrl = "http://www.testing.chroniclerewritten.com/api/";
                SplashBackground.Source = new BitmapImage(new Uri("pack://application:,,,/ChronicleLauncher;component/Images/Backgrounds/TestingSplash.jpg"));
                Testing_Text.Visibility = Visibility.Visible;
            }
            else
            {
                chronicleBaseUrl = "http://www.chroniclerewritten.com/";
                chronicleApiUrl = "http://www.chroniclerewritten.com/api/";
                SplashBackground.Source = new BitmapImage(new Uri("pack://application:,,,/ChronicleLauncher;component/Images/Backgrounds/Splash.jpg"));
                Testing_Text.Visibility = Visibility.Hidden;
            }
        }

        // Click methods
        private void PlayButton_Click(object sender, EventArgs e)
        {
            if(readyToPlay)
            {
                PlayButtonWrapper();
            }

            if(readyToDownload)
            {
                DownloadGameAsync();
            }
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

        // Functional Methods
        void ReportProgress(float value)
        {
            //Update the UI to reflect the progress value that is passed back.
            Download_Progress_Label.Text = " " + Math.Truncate(((value / Download_Progress.Maximum) * 100)).ToString() + "%";
            Download_Progress.Value = value;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
        }

        private static async Task<SVersionData> GetGameUrlAsync(string version)
        {
            SVersionData gameVersionData = new();
            HttpClient versionClient = new();
            try
            {
                versionClient.BaseAddress = new Uri(chronicleApiUrl);
                versionClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                Dictionary<string, object> requestData = new()
                {
                    { "route", "downloadgameversion" }
                };

                Dictionary<string, string> innerData = new()
                {
                    { "version_id", version },
                    { "url_id", "3" }
                };

                requestData.Add("data", innerData);

                var jsonContent = JsonSerializer.Serialize(requestData);
                var requestContent = new StringContent($"data={Uri.EscapeDataString(jsonContent)}", Encoding.UTF8, "application/x-www-form-urlencoded");

                HttpResponseMessage response = await versionClient.PostAsync("", requestContent);

                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();

                    JsonNode? jo = JsonNode.Parse(jsonString);
                    if (jo != null)
                    {
                        JsonNode? urlNode = jo["download_url"];
                        if (urlNode != null)
                        {
                            gameVersionData.s_zipName = urlNode.GetValue<string>();
                            int pFrom = gameVersionData.s_zipName.LastIndexOf("/") + 1;
                            int pTo = gameVersionData.s_zipName.LastIndexOf(".");
                            gameVersionData.s_unzipName = gameVersionData.s_zipName[pFrom..pTo]; ;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while retrieving the file size: {ex.Message}");
            }
            return gameVersionData;
        }

        private static async Task<SVersionData> GetLatestGameVersionAsync()
        {
            SVersionData returnData = new();

            HttpClient versionClient = new()
            {
                BaseAddress = new Uri(chronicleApiUrl)
            };
            versionClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));


            Dictionary<string, string> requestData = new()
            {
                { "route", "getgameversions" }
            };
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
                                    var zipUrl = urlNode.GetValue<string>();
                                    int pFrom = zipUrl.LastIndexOf("/") + 1;
                                    int pTo = zipUrl.LastIndexOf(".");
                                    returnData.s_unzipName = zipUrl[pFrom..pTo];
                                }

                                JsonNode? nameNode = latestGameVersion["name"];
                                if (nameNode != null)
                                {
                                    returnData.s_versionString = nameNode.GetValue<string>();
                                    m_versionString = returnData.s_versionString;
                                }

                                JsonNode? idNode = latestGameVersion["id"];
                                if (idNode != null)
                                {
                                    m_versionID = idNode.GetValue<string>();
                                }
                            }
                        }
                    }
                }
            }
            versionClient.Dispose();
            return returnData;
        }

        public static async Task<long> GetFileSizeAsync(string url)
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

        private async Task StartChronicleAsync(string latestExecutableLocation)
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

                    await exeProcess.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Problem launching Chronicle: " + ex.ToString());
            }
        }

        private static async Task<SVersionData> GetLatestLauncherVersionAsync()
        {
            SVersionData returnData = new();
            HttpClient versionClient = new();
            try
            {
                versionClient.BaseAddress = new Uri(chronicleApiUrl);
                versionClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));

                Dictionary<string, string> requestData = new()
                {
                    { "route", "getlauncherversions" }
                };

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
                                    JsonNode? nameNode = latestGameVersion["name"];
                                    if (nameNode != null)
                                    {
                                        returnData.s_versionString = nameNode.GetValue<string>();
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
