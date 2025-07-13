using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
        private readonly CleanerPaths paths = new();
        private readonly string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private CancellationTokenSource cancellationTokenSource;
        private bool isCleaning;

        public MainWindow()
        {
            InitializeComponent();
            LoadPaths();
            InitializeCategoryButtons();
        }

        private void InitializeCategoryButtons()
        {
            var categories = new Dictionary<string, List<string>>
            {
                { "Temp", paths.Temp },
                { "System", paths.SystemFolders },
                { "ProgramData", paths.ProgramData },
                { "User", paths.User },
                { "Roaming", paths.Roaming },
                { "Local", paths.Local },
                { "LocalLow", paths.LocalLow },
                { "Browser", paths.Browser }
            };

            foreach (var category in categories)
            {
                var button = new Button
                {
                    Content = category.Key,
                    Width = 120,
                    Margin = new Thickness(5),
                    Background = (Brush)new BrushConverter().ConvertFrom("#FFC76565"),
                    Tag = category.Value
                };
                button.Click += async (s, e) => await CleanCategory(button);
                CategoryButtonsControl.Items.Add(button);
            }
        }

        private void LoadPaths()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "winclean.paths.json";

                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    throw new FileNotFoundException("Embedded resource not found", resourceName);

                using StreamReader reader = new(stream);
                string json = reader.ReadToEnd();

                var loadedPaths = JsonSerializer.Deserialize<CleanerPaths>(json);
                if (loadedPaths != null)
                {
                    paths.Temp = loadedPaths.Temp;
                    paths.ProgramData = loadedPaths.ProgramData;
                    paths.SystemFolders = loadedPaths.SystemFolders;
                    paths.User = loadedPaths.User;
                    paths.Roaming = loadedPaths.Roaming;
                    paths.Local = loadedPaths.Local;
                    paths.LocalLow = loadedPaths.LocalLow;
                    paths.Browser = loadedPaths.Browser;

                    ReplaceUserProfile();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading paths: {ex.Message}", "Configuration Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReplaceUserProfile()
        {
            void ProcessList(List<string> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i] = list[i].Replace("%USERPROFILE%", userProfile);
                }
            }

            ProcessList(paths.Temp);
            ProcessList(paths.ProgramData);
            ProcessList(paths.SystemFolders);
            ProcessList(paths.User);
            ProcessList(paths.Roaming);
            ProcessList(paths.Local);
            ProcessList(paths.LocalLow);
            ProcessList(paths.Browser);
        }

        private async Task CleanCategory(Button button)
        {
            if (isCleaning) return;

            var categoryName = button.Content.ToString();
            var paths = (List<string>)button.Tag;

            await Clean(paths, categoryName);
        }

        private async void AllButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCleaning) return;

            var result = MessageBox.Show(
                "Do you really want to delete all temporary folders?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var all = paths.Temp
                .Concat(paths.SystemFolders)
                .Concat(paths.ProgramData)
                .Concat(paths.User)
                .Concat(paths.Roaming)
                .Concat(paths.Local)
                .Concat(paths.LocalLow)
                .Concat(paths.Browser)
                .ToList();

            await Clean(all, "ALL CATEGORIES");
        }

        private async Task Clean(List<string> paths, string categoryName)
        {
            isCleaning = true;
            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            try
            {
                CleanAllButton.IsEnabled = false;
                StatusText.Text = $"Cleaning {categoryName}...";
                LogBox.Clear();
                Log($"🧹 Cleaning {categoryName} started...\n");

                long sizeBefore = await CalculateTotalSizeAsync(paths, token);
                long deletedBytes = await DeleteFilesAndFoldersAsync(paths, token);
                long sizeAfter = await CalculateTotalSizeAsync(paths, token);

                long actualDeletedBytes = sizeBefore - sizeAfter;
                if (actualDeletedBytes < 0) actualDeletedBytes = 0;

                Log($"\n✅ Cleaning {categoryName} finished. " +
                    $"Cleaned {FormatBytes(actualDeletedBytes)} " +
                    $"({FormatBytes(deletedBytes)} reclaimed).");
            }
            catch (OperationCanceledException)
            {
                Log($"\n⚠️ Cleaning {categoryName} was cancelled.");
            }
            catch (Exception ex)
            {
                Log($"\n❌ Error during cleaning: {ex.Message}");
            }
            finally
            {
                StatusText.Text = "";
                isCleaning = false;
                CleanAllButton.IsEnabled = true;
                cancellationTokenSource.Dispose();
            }
        }

        private async Task<long> DeleteFilesAndFoldersAsync(List<string> folders, CancellationToken token)
        {
            long totalDeletedBytes = 0;
            var progress = new Progress<string>(Log);

            await Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                try
                {
                    Parallel.ForEach(folders, options, folder =>
                    {
                        if (token.IsCancellationRequested) return;
                        totalDeletedBytes += DeleteFolderContents(folder, token);
                    });
                }
                catch (OperationCanceledException) { }
            });

            return totalDeletedBytes;
        }

        private long DeleteFolderContents(string folderPath, CancellationToken token)
        {
            if (!Directory.Exists(folderPath)) return 0;

            long deletedBytes = 0;
            try
            {
                // Delete files
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) return deletedBytes;

                    try
                    {
                        var info = new FileInfo(file);
                        if (info.IsReadOnly)
                            info.IsReadOnly = false;

                        File.Delete(file);
                        deletedBytes += info.Length;
                        Dispatcher.Invoke(() => Log($"[✓] Deleted: {file}"));
                    }
                    catch (Exception ex)
                    {
                       
                    }
                }

                // Delete directories
                var directories = Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories)
                                           .OrderByDescending(d => d.Length);

                foreach (var dir in directories)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir, false);
                            Dispatcher.Invoke(() => Log($"[✓] Deleted empty folder: {dir}"));
                        }
                    }
                    catch (Exception ex)
                    {
                      
                    }
                }
            }
            catch (Exception ex)
            {
               
            }

            return deletedBytes;
        }

        private async Task<long> CalculateTotalSizeAsync(List<string> folders, CancellationToken token)
        {
            long totalSize = 0;
            await Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                Parallel.ForEach(folders, options, folder =>
                {
                    if (Directory.Exists(folder))
                    {
                        try
                        {
                            long size = CalculateFolderSize(folder);
                            Interlocked.Add(ref totalSize, size);
                            Dispatcher.Invoke(() => Log($"[ℹ] Size: {FormatBytes(size)} - {folder}"));
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => Log($"[!] Error calculating size for {folder}: {ex.Message}"));
                        }
                    }
                });
            });
            return totalSize;
        }

        private long CalculateFolderSize(string folderPath)
        {
            long size = 0;
            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        size += info.Length;
                    }
                    catch { /* Ignore individual file errors */ }
                }
            }
            catch { /* Ignore directory access errors */ }
            return size;
        }

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => Log(message));
                return;
            }

            LogBox.AppendText($"{message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            if (bytes == 0) return "0 B";

            int magnitude = (int)Math.Log(bytes, 1024);
            double adjustedSize = bytes / Math.Pow(1024, magnitude);

            return $"{adjustedSize:0.##} {sizes[magnitude]}";
        }

        private async void CalculateSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (isCleaning) return;

            isCleaning = true;
            CleanAllButton.IsEnabled = false;

            try
            {
                LogBox.Clear();
                Log("🔎 Calculating total size of all folders...\n");

                var allPaths = paths.Temp.Concat(paths.SystemFolders)
                    .Concat(paths.ProgramData)
                    .Concat(paths.User)
                    .Concat(paths.Roaming)
                    .Concat(paths.Local)
                    .Concat(paths.LocalLow)
                    .Concat(paths.Browser)
                    .ToList();

                long totalSize = await CalculateTotalSizeAsync(allPaths, CancellationToken.None);
                Log($"\n📊 Total size: {FormatBytes(totalSize)}");
            }
            finally
            {
                isCleaning = false;
                CleanAllButton.IsEnabled = true;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cancellationTokenSource?.Cancel();

            if (isCleaning)
            {
                var result = MessageBox.Show(
                    "Cleaning is in progress. Are you sure you want to exit?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                e.Cancel = (result != MessageBoxResult.Yes);
            }
        }
    }
}