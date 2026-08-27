using System.Diagnostics;

namespace MediaConverter;

internal sealed partial class ConverterForm
{
    private void AddFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Filter = "Media files|*.mp3;*.m4a;*.mp4;*.ogg;*.wav;*.flac;*.aac;*.wma;*.webm;*.mkv;*.mov;*.avi|All files|*.*" };
        if (!string.IsNullOrWhiteSpace(settings.LastInputFolder) && Directory.Exists(settings.LastInputFolder))
        {
            dialog.InitialDirectory = settings.LastInputFolder;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(settings.LastInputFolder) && Directory.Exists(settings.LastInputFolder))
        {
            dialog.SelectedPath = settings.LastInputFolder;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths([dialog.SelectedPath]);
        }
    }

    private void ChooseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(settings.LastOutputFolder) && Directory.Exists(settings.LastOutputFolder))
        {
            dialog.SelectedPath = settings.LastOutputFolder;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputFolderText.Text = dialog.SelectedPath;
        }
    }

    private void ChooseFfmpegPath()
    {
        using var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|Executable files|*.exe|All files|*.*", Title = "Choose ffmpeg.exe" };
        var detected = GetFfmpegPath();
        if (!string.IsNullOrWhiteSpace(detected) && File.Exists(detected))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(detected);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ffmpegPathText.Text = dialog.FileName;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var known = queue.Select(item => item.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = new List<string>();
        var ffprobe = GetFfprobePath();
        var addedItems = new List<QueueItem>();
        foreach (var path in paths)
        {
            foreach (var fileResult in GetMediaFiles(path))
            {
                if (fileResult.File is null)
                {
                    if (!string.IsNullOrWhiteSpace(fileResult.Message))
                    {
                        skipped.Add(fileResult.Message);
                    }
                    continue;
                }
                var file = fileResult.File;
                if (known.Add(file.FullName))
                {
                    var item = new QueueItem { Source = file.FullName, SourceBytes = file.Length, Duration = string.IsNullOrWhiteSpace(ffprobe) ? "" : "Loading...", RootFolder = fileResult.RootFolder };
                    queue.Add(item);
                    addedItems.Add(item);
                    RememberFolder(file.DirectoryName ?? "");
                    added++;
                }
            }
        }
        RefreshRecentFolders();
        RefreshGrid();
        if (added == 0)
        {
            MessageBox.Show(this, "No new supported media files were found.", "Media Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (skipped.Count > 0)
        {
            MessageBox.Show(this, $"Added {added} file(s). Some folders/files were skipped:\n\n{string.Join("\n", skipped.Take(8))}", "Media Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        if (addedItems.Count > 0)
        {
            _ = LoadDurationsAsync(addedItems);
        }
    }

    private List<(FileInfo? File, string RootFolder, string Message)> GetMediaFiles(string path)
    {
        var results = new List<(FileInfo? File, string RootFolder, string Message)>();
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            if (SupportedInputs.Contains(file.Extension))
            {
                results.Add((file, file.DirectoryName ?? "", ""));
            }
            return results;
        }
        if (!Directory.Exists(path))
        {
            return results;
        }
        RememberFolder(path);
        var pendingFolders = new Stack<string>();
        pendingFolders.Push(path);
        while (pendingFolders.Count > 0)
        {
            var folder = pendingFolders.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                results.Add((null, path, $"Skipped folder {folder}: {ex.Message}"));
                continue;
            }

            foreach (var filePath in files)
            {
                FileInfo file;
                try
                {
                    file = new FileInfo(filePath);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    results.Add((null, path, $"Skipped file {filePath}: {ex.Message}"));
                    continue;
                }
                if (SupportedInputs.Contains(file.Extension))
                {
                    results.Add((file, path, ""));
                }
            }

            if (!includeSubfoldersCheck.Checked)
            {
                continue;
            }

            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                results.Add((null, path, $"Skipped subfolders under {folder}: {ex.Message}"));
                continue;
            }

            foreach (var subfolder in subfolders)
            {
                pendingFolders.Push(subfolder);
            }
        }
        return results;
    }

    private void RememberFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }
        settings.LastInputFolder = folder;
        settings.RecentFolders.RemoveAll(existing => existing.Equals(folder, StringComparison.OrdinalIgnoreCase));
        settings.RecentFolders.Insert(0, folder);
        if (settings.RecentFolders.Count > 10)
        {
            settings.RecentFolders.RemoveRange(10, settings.RecentFolders.Count - 10);
        }
        SaveSettingsFromControls();
    }

    private void RefreshRecentFolders()
    {
        var selected = recentFolderCombo.SelectedItem?.ToString();
        recentFolderCombo.Items.Clear();
        foreach (var folder in settings.RecentFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            recentFolderCombo.Items.Add(folder);
        }
        if (!string.IsNullOrWhiteSpace(selected) && recentFolderCombo.Items.Contains(selected))
        {
            recentFolderCombo.SelectedItem = selected;
        }
    }

    private void RefreshGrid(bool updateStatus = true)
    {
        UpdateQueueDestinations();
        grid.Rows.Clear();
        foreach (var item in queue)
        {
            grid.Rows.Add(item.Status, item.Duration, item.Source, item.Destination, item.Message);
        }
        var pending = queue.Count(item => item.Status == "Pending");
        if (updateStatus)
        {
            statusLabel.Text = $"Queue: {queue.Count} file(s), {pending} ready";
        }
    }

    private void UpdateQueueDestinations()
    {
        var destinations = new Dictionary<string, List<QueueItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in queue)
        {
            item.Destination = GetOutputPath(item.Source, item.RootFolder);
            if (!destinations.TryGetValue(item.Destination, out var items))
            {
                items = [];
                destinations[item.Destination] = items;
            }
            items.Add(item);
        }
        foreach (var item in queue)
        {
            var sourceFull = Path.GetFullPath(item.Source);
            var destinationFull = Path.GetFullPath(item.Destination);
            var collidingItems = destinations[item.Destination];
            if (sourceFull.Equals(destinationFull, StringComparison.OrdinalIgnoreCase))
            {
                item.Status = "Skipped";
                item.Message = "Input already matches selected output";
            }
            else if (collidingItems.Count > 1 && namingCombo.SelectedItem?.ToString() != "Auto-number conflicts")
            {
                item.Status = "Skipped";
                item.Message = "Output path conflicts with another queued item";
            }
            else if (File.Exists(item.Destination) && !overwriteCheck.Checked)
            {
                item.Status = "Skipped";
                item.Message = "Output already exists";
            }
            else if (item.Status is "Skipped" or "Pending")
            {
                item.Status = "Pending";
                item.Message = "";
            }
        }
    }

    private string GetOutputPath(string source, string rootFolder)
    {
        var format = (formatCombo.SelectedItem?.ToString() ?? "mp3").ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(source);
        var namingRule = namingCombo.SelectedItem?.ToString() ?? "Same name";
        baseName = ConversionLogic.BuildBaseName(baseName, namingRule);
        var fileName = baseName + "." + format;
        var directory = Path.GetDirectoryName(source) ?? "";
        if (useOutputFolderCheck.Checked)
        {
            directory = outputFolderText.Text.Trim();
            if (preserveFoldersCheck.Checked && !string.IsNullOrWhiteSpace(rootFolder))
            {
                directory = Path.Combine(directory, ConversionLogic.GetRelativeDirectory(rootFolder, Path.GetDirectoryName(source) ?? ""));
            }
        }
        var destination = Path.Combine(directory, fileName);
        return namingRule == "Auto-number conflicts" && (!overwriteCheck.Checked || queue.Count > 1) ? GetUniqueDestination(destination, source) : destination;
    }

    private string GetUniqueDestination(string destination, string source)
    {
        var reserved = queue.Where(item => !item.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).Select(item => item.Destination).Where(path => !string.IsNullOrWhiteSpace(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(destination) && !reserved.Contains(destination))
        {
            return destination;
        }
        var directory = Path.GetDirectoryName(destination) ?? "";
        var name = Path.GetFileNameWithoutExtension(destination);
        var extension = Path.GetExtension(destination);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            index++;
        } while (File.Exists(candidate) || reserved.Contains(candidate));
        return candidate;
    }

    private string[] GetEncoderArgs(string format) =>
        ConversionLogic.GetEncoderArgs(format, qualityCombo.SelectedItem?.ToString() ?? "Balanced", customAudioText.Text);

    private string? GetFfmpegPath()
    {
        var configured = ffmpegPathText.Text.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        return FindExecutableOnPath("ffmpeg.exe");
    }

    private string? GetFfprobePath()
    {
        var ffmpeg = GetFfmpegPath();
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }
        return FindExecutableOnPath("ffprobe.exe");
    }

    private static string? FindExecutableOnPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private void UpdateFfmpegStatus()
    {
        var ffmpeg = GetFfmpegPath();
        statusLabel.Text = ffmpeg is null ? "ffmpeg not found. Use the ffmpeg button to choose ffmpeg.exe, or Install ffmpeg." : $"Ready. ffmpeg: {ffmpeg}";
        installFfmpegButton.Visible = ffmpeg is null;
    }

    private static string GetDurationText(string? ffprobe, string file)
    {
        if (string.IsNullOrWhiteSpace(ffprobe) || !File.Exists(ffprobe))
        {
            return "";
        }
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo { FileName = ffprobe, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", file })
            {
                process.StartInfo.ArgumentList.Add(arg);
            }
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2500);
            return double.TryParse(output, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "";
        }
        catch (Exception ex)
        {
            Logger.Warn("GetDurationText", ex);
            return "";
        }
    }

    private async Task LoadDurationsAsync(List<QueueItem> items)
    {
        var ffprobe = GetFfprobePath();
        if (string.IsNullOrWhiteSpace(ffprobe) || !File.Exists(ffprobe))
        {
            return;
        }

        foreach (var item in items)
        {
            if (!queue.Contains(item))
            {
                continue;
            }

            var duration = await Task.Run(() => GetDurationText(ffprobe, item.Source));
            if (!queue.Contains(item) || IsDisposed || !IsHandleCreated)
            {
                continue;
            }

            BeginInvoke(new Action(() =>
            {
                if (queue.Contains(item))
                {
                    item.Duration = duration;
                    RefreshGrid(updateStatus: false);
                }
            }));
        }
    }
}
