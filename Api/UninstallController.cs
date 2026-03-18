using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.YouTubeFeed.Api;

/// <summary>
/// Handles cleanup of all resources added by this plugin (proxy service, files).
/// Called by the "Uninstall Plugin" button on the configuration page.
/// </summary>
[ApiController]
[Route("youtubefeed")]
[Authorize(Policy = "RequiresElevation")]
public class UninstallController : ControllerBase
{
    private readonly ILogger<UninstallController> _logger;

    public UninstallController(ILogger<UninstallController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Stops and removes the ytstream-proxy systemd service and its files.
    /// The caller is responsible for subsequently uninstalling the plugin via the Jellyfin API.
    /// </summary>
    [HttpPost("cleanup")]
    public IActionResult Cleanup()
    {
        try
        {
            // Stop and disable the service (ignore errors if it isn't running/installed)
            RunCommand("systemctl", "stop ytstream-proxy");
            RunCommand("systemctl", "disable ytstream-proxy");

            // Remove the service file and its enable symlink
            TryDelete("/etc/systemd/system/ytstream-proxy.service");
            TryDelete("/etc/systemd/system/multi-user.target.wants/ytstream-proxy.service");

            RunCommand("systemctl", "daemon-reload");

            // Remove the user-configured .strm feed folder
            var strmFolder = Plugin.Instance?.Configuration.StrmFolderPath;
            if (!string.IsNullOrWhiteSpace(strmFolder) && Directory.Exists(strmFolder))
                Directory.Delete(strmFolder, recursive: true);

            // Remove the plugin data folder
            var dataFolder = Plugin.Instance?.DataFolderPath;
            if (dataFolder != null && Directory.Exists(dataFolder))
                Directory.Delete(dataFolder, recursive: true);

            // Remove the plugin configuration XML
            var configFile = Plugin.Instance?.ConfigurationFilePath;
            if (configFile != null && System.IO.File.Exists(configFile))
                System.IO.File.Delete(configFile);

            _logger.LogInformation("YouTubeFeed: proxy cleanup completed successfully");
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTubeFeed: proxy cleanup failed");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    private void RunCommand(string filename, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("YouTubeFeed: command '{Cmd} {Args}' failed: {Err}", filename, arguments, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }
}
