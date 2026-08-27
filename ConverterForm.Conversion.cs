using System.Diagnostics;
using System.Text;

namespace MediaConverter;

internal sealed partial class ConverterForm
{
    private async Task ConvertQueueAsync()
    {
        if (isConverting)
        {
            return;
        }
        var ffmpeg = GetFfmpegPath();
        if (ffmpeg is null)
        {
            MessageBox.Show(this, "ffmpeg was not found. Open Settings (⚙) and use Browse to choose ffmpeg.exe, or use Install ffmpeg on the status bar.", "Missing ffmpeg", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (useOutputFolderCheck.Checked && !Directory.Exists(outputFolderText.Text))
        {
            MessageBox.Show(this, "Choose a valid output folder, or turn off 'Use one output folder'.", "Invalid output folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        UpdateQueueDestinations();
        var work = queue.Where(item => item.Status == "Pending").ToList();
        if (work.Count == 0)
        {
            MessageBox.Show(this, "Nothing is ready to convert.", "Media Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        isConverting = true;
        pauseRequested = false;
        conversionCancellation = new CancellationTokenSource();
        convertButton.Enabled = false;
        cancelButton.Enabled = true;
        pauseButton.Enabled = true;
        pauseButton.Text = "Pause";
        progressBar.Minimum = 0;
        progressBar.Maximum = work.Count;
        progressBar.Value = 0;
        var format = formatCombo.SelectedItem?.ToString() ?? "mp3";
        var encoderArgs = GetEncoderArgs(format);
        var logRows = new List<object>();

        try
        {
            for (var index = 0; index < work.Count; index++)
            {
                if (conversionCancellation.IsCancellationRequested)
                {
                    break;
                }
                while (pauseRequested && !conversionCancellation.IsCancellationRequested)
                {
                    statusLabel.Text = "Paused. Click Resume to continue.";
                    await Task.Delay(200);
                }
                var item = work[index];
                item.Status = "Converting";
                item.Message = "";
                RefreshGrid();
                statusLabel.Text = $"Converting {index + 1} of {work.Count}: {Path.GetFileName(item.Source)}";
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination) ?? ".");
                var overwriteArg = overwriteCheck.Checked ? "-y" : "-n";
                var args = new List<string> { "-hide_banner", "-loglevel", "error", overwriteArg, "-i", item.Source, "-vn", "-map", "0:a:0" };
                args.AddRange(encoderArgs);
                args.Add(item.Destination);
                var started = DateTime.Now;
                var result = await RunProcessAsync(ffmpeg, args, conversionCancellation.Token);
                var ended = DateTime.Now;
                if (conversionCancellation.IsCancellationRequested)
                {
                    item.Status = "Cancelled";
                    item.Message = "Cancelled by user";
                }
                else if (result.ExitCode == 0 && File.Exists(item.Destination) && new FileInfo(item.Destination).Length > 0)
                {
                    item.Status = "Converted";
                    item.Message = "";
                    if (deleteOriginalsCheck.Checked)
                    {
                        await TryDeleteOriginalAsync(item);
                    }
                }
                else
                {
                    item.Status = "Failed";
                    item.Message = string.IsNullOrWhiteSpace(result.Output) ? "ffmpeg failed without output" : result.Output.Trim();
                }
                logRows.Add(new { item.Source, item.Destination, OutputFormat = format, QualityPreset = qualityCombo.SelectedItem?.ToString() ?? "", item.Status, item.Message, StartedAt = started, EndedAt = ended, result.ExitCode });
                progressBar.Value = index + 1;
                RefreshGrid();
            }
            lastLogPath = WriteRunLog(logRows);
            var converted = queue.Count(item => item.Status == "Converted");
            var failed = queue.Count(item => item.Status == "Failed");
            var skipped = queue.Count(item => item.Status == "Skipped");
            var cancelled = queue.Count(item => item.Status == "Cancelled");
            UpdateRunSummary(converted, failed, skipped, cancelled);
            statusLabel.Text = $"Done. Converted: {converted}, failed: {failed}, skipped: {skipped}, cancelled: {cancelled}. Log: {lastLogPath}";
        }
        finally
        {
            activeProcess = null;
            conversionCancellation?.Dispose();
            conversionCancellation = null;
            convertButton.Enabled = true;
            cancelButton.Enabled = false;
            pauseButton.Enabled = false;
            pauseButton.Text = "Pause";
            pauseRequested = false;
            isConverting = false;
            SaveSettingsFromControls();
        }
    }

    /// <summary>
    /// Deletes the source file only after confirming the output is playable media, not just non-empty.
    /// ffmpeg can exit 0 and still write a truncated/corrupt file; a bare length check would delete
    /// the only good copy in that case. Falls back to the length-only check when ffprobe isn't available.
    /// </summary>
    private async Task TryDeleteOriginalAsync(QueueItem item)
    {
        try
        {
            var output = new FileInfo(item.Destination);
            if (!output.Exists || output.Length <= 0 || item.Source.Equals(item.Destination, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var ffprobe = GetFfprobePath();
            if (!string.IsNullOrWhiteSpace(ffprobe) && File.Exists(ffprobe))
            {
                var valid = await IsValidMediaFileAsync(ffprobe, item.Destination, conversionCancellation?.Token ?? CancellationToken.None);
                if (!valid)
                {
                    item.Message = "Converted, but output failed validation - original kept";
                    Logger.Warn("TryDeleteOriginalAsync", new InvalidOperationException($"Output failed ffprobe validation: {item.Destination}"));
                    return;
                }
            }

            File.Delete(item.Source);
            item.Message = "Original deleted";
        }
        catch (Exception ex)
        {
            item.Message = $"Converted, but original delete failed: {ex.Message}";
            Logger.Warn("TryDeleteOriginalAsync", ex);
        }
    }

    /// <summary>
    /// Runs ffprobe against the freshly-written output and confirms it reports a real duration.
    /// Reads stdout/stderr concurrently (not sequential ReadToEnd calls) to avoid the classic
    /// .NET pipe deadlock if ffprobe ever writes enough to stderr to fill its buffer, and is
    /// bounded by a timeout plus the conversion's own cancellation token so a hung probe can't
    /// leave the app looking frozen with no way to stop it.
    /// </summary>
    private static async Task<bool> IsValidMediaFileAsync(string ffprobe, string file, CancellationToken token)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo { FileName = ffprobe, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", file })
            {
                process.StartInfo.ArgumentList.Add(arg);
            }
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                return false;
            }

            var output = (await stdoutTask).Trim();
            var error = (await stderrTask).Trim();
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
            {
                return false;
            }
            return double.TryParse(output, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0;
        }
        catch
        {
            return false;
        }
    }

    private void CancelConversion()
    {
        conversionCancellation?.Cancel();
        try
        {
            if (activeProcess is { HasExited: false })
            {
                activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("CancelConversion", ex);
        }
        statusLabel.Text = "Cancelling...";
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> args, CancellationToken token)
    {
        var output = new StringBuilder();
        using var process = new Process();
        activeProcess = process;
        process.StartInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            return new ProcessResult { ExitCode = -1, Output = "Cancelled by user" };
        }
        finally
        {
            if (ReferenceEquals(activeProcess, process))
            {
                activeProcess = null;
            }
        }
        return new ProcessResult { ExitCode = process.ExitCode, Output = output.ToString() };
    }

    private string WriteRunLog(IEnumerable<object> rows)
    {
        var logRoot = GetLogRoot();
        Directory.CreateDirectory(logRoot);
        var path = Path.Combine(logRoot, $"conversion-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var lines = new List<string> { "Source,Destination,OutputFormat,QualityPreset,Status,Message,StartedAt,EndedAt,ExitCode" };
        foreach (dynamic row in rows)
        {
            lines.Add(string.Join(",", new[]
            {
                ConversionLogic.Csv(row.Source), ConversionLogic.Csv(row.Destination), ConversionLogic.Csv(row.OutputFormat),
                ConversionLogic.Csv(row.QualityPreset), ConversionLogic.Csv(row.Status), ConversionLogic.Csv(row.Message),
                ConversionLogic.Csv(row.StartedAt.ToString("O")), ConversionLogic.Csv(row.EndedAt.ToString("O")), ConversionLogic.Csv(row.ExitCode.ToString())
            }));
        }
        File.WriteAllLines(path, lines, Encoding.UTF8);
        return path;
    }

    private static string GetLogRoot() => Path.Combine(GetLocalDataRoot(), "logs");

    private void OpenLogsFolder()
    {
        var logRoot = GetLogRoot();
        Directory.CreateDirectory(logRoot);
        Process.Start(new ProcessStartInfo(logRoot) { UseShellExecute = true });
    }

    private void OpenLastLog()
    {
        if (!string.IsNullOrWhiteSpace(lastLogPath) && File.Exists(lastLogPath))
        {
            Process.Start(new ProcessStartInfo(lastLogPath) { UseShellExecute = true });
            return;
        }
        OpenLogsFolder();
    }

    private void UpdateRunSummary(int converted, int failed, int skipped, int cancelled)
    {
        summaryLabel.Text = $"Last run: {converted} converted, {failed} failed, {skipped} skipped, {cancelled} cancelled.";
        openLastLogButton.Enabled = !string.IsNullOrWhiteSpace(lastLogPath) && File.Exists(lastLogPath);
        retryFailedButton.Enabled = queue.Any(item => item.Status is "Failed" or "Skipped" or "Cancelled");
        summaryClearConvertedButton.Enabled = queue.Any(item => item.Status == "Converted");
    }

    private void RemoveSelectedRows()
    {
        foreach (var item in GetSelectedQueueItems())
        {
            queue.Remove(item);
        }
        RefreshGrid();
    }

    private void RetrySelectedRows()
    {
        foreach (var item in GetSelectedQueueItems())
        {
            if (item.Status is "Failed" or "Skipped" or "Cancelled")
            {
                item.Status = "Pending";
                item.Message = "";
            }
        }
        RefreshGrid();
    }

    private void RetryFailedItems()
    {
        foreach (var item in queue)
        {
            if (item.Status is "Failed" or "Skipped" or "Cancelled")
            {
                item.Status = "Pending";
                item.Message = "";
            }
        }
        retryFailedButton.Enabled = false;
        RefreshGrid();
    }

    private void ClearConvertedItems()
    {
        queue.RemoveAll(item => item.Status == "Converted");
        summaryClearConvertedButton.Enabled = queue.Any(item => item.Status == "Converted");
        RefreshGrid();
    }

    private void ShowHelpDialog()
    {
        MessageBox.Show(
            this,
            "Drag files or folders into the top area, or use Add files/Add folder.\n\n" +
            "Default output is MP3 in the same folder as the source. Use Format and Quality to change the result - Quality doesn't apply to WAV/FLAC since they're lossless.\n\n" +
            "Quality presets choose common ffmpeg audio settings. Custom accepts values such as 192k for bitrate.\n\n" +
            "Click the ⚙ Settings button for naming rules, a custom output folder, ffmpeg's path, and safety options like Overwrite and Delete originals after success (which only removes a source file after ffmpeg succeeds and the output file passes validation).\n\n" +
            "Logs and settings are stored in LocalAppData\\MediaConverter. Use Settings > Clear app data to remove settings, logs, and ffmpeg downloaded by the app.",
            "Media Converter Help",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenSelectedFolder(bool source)
    {
        var item = GetSelectedQueueItems().FirstOrDefault();
        if (item is null)
        {
            return;
        }
        var path = source ? item.Source : item.Destination;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
    }

    private List<QueueItem> GetSelectedQueueItems()
    {
        var indexes = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).Where(index => index >= 0 && index < queue.Count).Distinct().OrderByDescending(index => index).ToList();
        return indexes.Select(index => queue[index]).ToList();
    }
}
