using System.Diagnostics;
using System.Runtime.InteropServices;
using CarnotCycleCircus.UI.Services;

namespace CarnotCycleCircus.Desktop.Services;

public class DesktopNativeFolderPicker : INativeFolderPicker
{
    public async Task<string?> PickDirectoryAsync(string? initialDirectory = null, string title = "Select Target Code Repository", CancellationToken cancellationToken = default)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await PickLinuxDirectoryAsync(initialDirectory, title, cancellationToken);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await PickMacDirectoryAsync(initialDirectory, title, cancellationToken);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await PickWindowsDirectoryAsync(initialDirectory, title, cancellationToken);
            }
        }
        catch
        {
            // Graceful fallback on any process launch or permission error
        }

        return initialDirectory;
    }

    private static async Task<string?> PickLinuxDirectoryAsync(string? initialDirectory, string title, CancellationToken cancellationToken)
    {
        // Try zenity first
        if (IsExecutableInPath("zenity"))
        {
            var initialArg = !string.IsNullOrWhiteSpace(initialDirectory) ? $"--filename=\"{initialDirectory}/\"" : "";
            var result = await RunProcessOutputAsync("zenity", $"--file-selection --directory --title=\"{title}\" {initialArg}", cancellationToken);
            if (!string.IsNullOrWhiteSpace(result) && Directory.Exists(result.Trim()))
            {
                return result.Trim();
            }
        }

        // Try kdialog
        if (IsExecutableInPath("kdialog"))
        {
            var initialArg = !string.IsNullOrWhiteSpace(initialDirectory) ? $"\"{initialDirectory}\"" : "";
            var result = await RunProcessOutputAsync("kdialog", $"--getexistingdirectory {initialArg} --title \"{title}\"", cancellationToken);
            if (!string.IsNullOrWhiteSpace(result) && Directory.Exists(result.Trim()))
            {
                return result.Trim();
            }
        }

        return initialDirectory;
    }

    private static async Task<string?> PickMacDirectoryAsync(string? initialDirectory, string title, CancellationToken cancellationToken)
    {
        var script = $"choose folder with prompt \"{title}\"";
        var result = await RunProcessOutputAsync("osascript", $"-e 'POSIX path of ({script})'", cancellationToken);
        if (!string.IsNullOrWhiteSpace(result) && Directory.Exists(result.Trim()))
        {
            return result.Trim();
        }
        return initialDirectory;
    }

    private static async Task<string?> PickWindowsDirectoryAsync(string? initialDirectory, string title, CancellationToken cancellationToken)
    {
        var psCommand = "[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = '" + title + "'; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output $f.SelectedPath }";
        var result = await RunProcessOutputAsync("powershell", $"-NoProfile -NonInteractive -Command \"{psCommand}\"", cancellationToken);
        if (!string.IsNullOrWhiteSpace(result) && Directory.Exists(result.Trim()))
        {
            return result.Trim();
        }
        return initialDirectory;
    }

    private static bool IsExecutableInPath(string exeName)
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            return paths.Any(p => File.Exists(Path.Combine(p, exeName)));
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> RunProcessOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var outputTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);
            return (await outputTask).Trim();
        }
        catch
        {
            return null;
        }
    }
}
