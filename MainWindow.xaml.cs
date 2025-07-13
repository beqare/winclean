using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Shapes;

namespace WinClean
{
    public class CleanerPaths
    {
        public List<string> Temp { get; set; } = new();
        public List<string> ProgramData { get; set; } = new();
        public List<string> SystemFolders { get; set; } = new();
        public List<string> User { get; set; } = new();
        public List<string> Roaming { get; set; } = new();
        public List<string> Local { get; set; } = new();
        public List<string> LocalLow { get; set; } = new();
        public List<string> Browser { get; set; } = new();
    }

    public partial class MainWindow : Window
    {
        private CleanerPaths paths = new();
        private readonly string user = Environment.UserName;
        private readonly string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public MainWindow()
        {
            InitializeComponent();
            LoadPaths();
        }

        

        private void LoadPaths()
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "winclean.paths.json"; // Namespace + Dateiname

                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        throw new Exception("Embedded resource nicht gefunden: " + resourceName);

                    using StreamReader reader = new(stream);
                    string json = reader.ReadToEnd();

                    paths = JsonSerializer.Deserialize<CleanerPaths>(json) ?? new CleanerPaths();

                    ReplaceUserProfile(paths);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Fehler beim Laden der Pfade: " + ex.Message);
                    paths = new CleanerPaths();
                }
            }


    private void ReplaceUserProfile(CleanerPaths paths)
        {
            void ReplaceInList(List<string> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Contains("%USERPROFILE%"))
                        list[i] = list[i].Replace("%USERPROFILE%", userProfile);
                }
            }

            ReplaceInList(paths.Temp);
            ReplaceInList(paths.ProgramData);
            ReplaceInList(paths.SystemFolders);
            ReplaceInList(paths.User);
            ReplaceInList(paths.Roaming);
            ReplaceInList(paths.Local);
            ReplaceInList(paths.LocalLow);
            ReplaceInList(paths.Browser);
        }

        private async void TempButton_Click(object s, RoutedEventArgs e) => await Clean(paths.Temp.ToArray());
        private async void SystemButton_Click(object s, RoutedEventArgs e) => await Clean(paths.SystemFolders.ToArray());
        private async void ProgramDataButton_Click(object s, RoutedEventArgs e) => await Clean(paths.ProgramData.ToArray());
        private async void UserButton_Click(object s, RoutedEventArgs e) => await Clean(paths.User.ToArray());
        private async void RoamingButton_Click(object s, RoutedEventArgs e) => await Clean(paths.Roaming.ToArray());
        private async void LocalButton_Click(object s, RoutedEventArgs e) => await Clean(paths.Local.ToArray());
        private async void LocalLowButton_Click(object s, RoutedEventArgs e) => await Clean(paths.LocalLow.ToArray());
        private async void BrowserButton_Click(object s, RoutedEventArgs e) => await Clean(paths.Browser.ToArray());

        private async void AllButton_Click(object s, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Do you really want to delete all temporary folders?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var all = paths.Temp
                .Concat(paths.SystemFolders)
                .Concat(paths.ProgramData)
                .Concat(paths.User)
                .Concat(paths.Roaming)
                .Concat(paths.Local)
                .Concat(paths.LocalLow)
                .Concat(paths.Browser)
                .ToArray();

            await Clean(all);
        }

        private async Task Clean(string[] paths)
        {
            LogBox.Clear();
            Log("🧹 Cleaning started...\n");

            long sizeBefore = await Task.Run(() => GetTotalSize(paths));

            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    TryDeleteFilesInFolder(path);
                }
            });

            long sizeAfter = await Task.Run(() => GetTotalSize(paths));

            long deletedBytes = sizeBefore - sizeAfter;
            if (deletedBytes < 0) deletedBytes = 0; // Falls Größe sich erhöht hat (neue Dateien)

            Log($"\n✅ Cleaning finished. Cleaned {FormatBytes(deletedBytes)}.");
        }


        private long TryDeleteFilesInFolder(string folderPath)
        {
            long deletedBytes = 0;

            if (!Directory.Exists(folderPath))
                return 0;

            try
            {
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        long fileSize = info.Length;

                        File.Delete(file);
                        deletedBytes += fileSize;
                        Log($"[✓] Deleted file: {file} ({FormatBytes(fileSize)})");
                    }
                    catch
                    {
                    }
                }

                try
                {
                    var dirs = Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length);
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                            {
                                Directory.Delete(dir, false);
                            }
                        }
                        catch { }
                    }
                    if (Directory.Exists(folderPath) && Directory.GetFileSystemEntries(folderPath).Length == 0)
                    {
                        Directory.Delete(folderPath, false);
                    }
                }
                catch { }
            }
            catch
            {
            }

            return deletedBytes;
        }

        private void Log(string msg)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogBox.AppendText(msg + Environment.NewLine);
                LogBox.ScrollToEnd();
            }));
        }
        private async void CalculateSizeButton_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
            Log("🔎 Berechne Gesamtgröße aller Ordner...\n");

            long totalBytes = 0;

            var allPaths = paths.Temp
                .Concat(paths.SystemFolders)
                .Concat(paths.ProgramData)
                .Concat(paths.User)
                .Concat(paths.Roaming)
                .Concat(paths.Local)
                .Concat(paths.LocalLow)
                .Concat(paths.Browser)
                .ToArray();

            await Task.Run(() =>
            {
                foreach (var path in allPaths)
                {
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            long size = GetDirectorySize(path);
                            totalBytes += size;
                            Log($"[ℹ] Size: {FormatBytes(size)} - {path}");
                        }
                        catch (Exception ex)
                        {
                         
                        }
                    }
                    else
                    {
                     
                    }
                }
            });

            Log($"\n📊 Total size of all folders: {FormatBytes(totalBytes)}");
        }

        private long GetDirectorySize(string folderPath)
        {
            long size = 0;
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    size += info.Length;
                }
                catch
                {
                }
            }
            return size;
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        private long GetTotalSize(string[] folders)
        {
            long totalSize = 0;
            foreach (var folder in folders)
            {
                if (Directory.Exists(folder))
                {
                    totalSize += GetDirectorySize(folder);
                }
            }
            return totalSize;
        }


    }
}
