using CarnotCycleCircus.Core.Extensions;
using CarnotCycleCircus.Desktop.Services;
using CarnotCycleCircus.UI.Components;
using CarnotCycleCircus.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Photino.NET;

namespace CarnotCycleCircus.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsLinux())
        {
            // WebKitGTK on modern Linux distributions (Wayland/X11 Mesa drivers) can experience
            // hardware acceleration DMA-BUF race conditions and crashes. Disabling DMABUF renderer
            // and compositing mode ensures rock-solid stability across all desktop environments.
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
            Environment.SetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
        }

        var baseDir = AppContext.BaseDirectory;
        var webRootDir = Path.Combine(baseDir, "wwwroot");

        if (!Directory.Exists(webRootDir))
        {
            var projectSourceDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            var candidateWebRoot = Path.Combine(projectSourceDir, "wwwroot");
            if (Directory.Exists(candidateWebRoot))
            {
                webRootDir = candidateWebRoot;
            }
        }

        // Configure in-process lightweight Kestrel web host on an ephemeral loopback port
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = baseDir,
            WebRootPath = Directory.Exists(webRootDir) ? webRootDir : null,
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseStaticWebAssets();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Add Razor components with Interactive Server mode
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Register Core Domain Services & Native Desktop Services
        builder.Services.AddCarnotCycleCircusCore();
        builder.Services.AddSingleton<INativeFolderPicker, DesktopNativeFolderPicker>();

        var app = builder.Build();

        app.UseAntiforgery();
        app.MapStaticAssets();
        app.UseStaticFiles();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Start Kestrel web host asynchronously
        app.Start();

        // Retrieve dynamically assigned localhost URL
        var serverAddresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>();
        var serverUrl = serverAddresses?.Addresses.FirstOrDefault() ?? "http://127.0.0.1:5000";

        // Create and configure native Photino Window
        var window = new PhotinoWindow()
            .SetTitle("Carnot Cycle Circus - Autonomous Swarm")
            .SetSize(1440, 920)
            .SetUseOsDefaultSize(false)
            .SetResizable(true)
            .Center()
            .Load(serverUrl);

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            Console.Error.WriteLine($"[Photino Desktop Error] {error.ExceptionObject}");
        };

        window.WaitForClose();

        // Gracefully shutdown host
        try
        {
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore shutdown cleanup exceptions
        }
    }
}
