using System.IO.Compression;
using System.Net.Http;

namespace MediaConverter;

internal sealed partial class ConverterForm
{
    // gyan.dev is the Windows static-build source ffmpeg.org itself links to on its download page.
    private const string FfmpegDownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private async Task InstallFfmpegAsync()
    {
        var confirm = MessageBox.Show(
            this,
            "This downloads a static ffmpeg build (~80 MB) from gyan.dev - the Windows build ffmpeg.org's own download page recommends - and installs it under your local app data. Continue?",
            "Install ffmpeg",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        installFfmpegButton.Enabled = false;
        var previousStatus = statusLabel.Text;
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaConverter", "ffmpeg");
        var zipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg-install-{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadFileAsync(FfmpegDownloadUrl, zipPath);

            statusLabel.Text = "Extracting ffmpeg...";
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
            Directory.CreateDirectory(installRoot);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, installRoot));

            var ffmpegExe = Directory.EnumerateFiles(installRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpegExe is null)
            {
                throw new FileNotFoundException("ffmpeg.exe was not found inside the downloaded archive.");
            }

            ffmpegPathText.Text = ffmpegExe;
            statusLabel.Text = "ffmpeg installed.";
        }
        catch (Exception ex)
        {
            Logger.Warn("InstallFfmpegAsync", ex);
            statusLabel.Text = previousStatus;
            MessageBox.Show(
                this,
                $"Could not install ffmpeg automatically: {ex.Message}\n\nYou can download it manually from https://ffmpeg.org/download.html and choose ffmpeg.exe with the ffmpeg button.",
                "Install failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn("InstallFfmpegAsync cleanup", ex);
                }
            }
            installFfmpegButton.Enabled = true;
            UpdateFfmpegStatus();
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        statusLabel.Text = "Downloading ffmpeg...";
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;

        await using var httpStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await httpStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            var downloadedMb = readTotal / 1024 / 1024;
            statusLabel.Text = totalBytes is > 0
                ? $"Downloading ffmpeg... {(int)(readTotal * 100 / totalBytes.Value)}% ({downloadedMb} MB)"
                : $"Downloading ffmpeg... {downloadedMb} MB";
        }
    }
}
