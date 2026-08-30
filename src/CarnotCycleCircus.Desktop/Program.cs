using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Extensions;
using CarnotCycleCircus.Desktop.Services;
using CarnotCycleCircus.UI.Components;
using CarnotCycleCircus.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace CarnotCycleCircus.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Register Core Domain Services & Native Desktop Services
        appBuilder.Services.AddCarnotCycleCircusCore();
        appBuilder.Services.AddSingleton<INativeFolderPicker, DesktopNativeFolderPicker>();

        // Register root component
        appBuilder.RootComponents.Add<DesktopApp>("#app");

        var app = appBuilder.Build();

        // Configure Photino Native Window
        app.MainWindow
            .SetTitle("🎪 Carnot Cycle Circus — Autonomous Multi-Agent Swarm")
            .SetSize(1440, 920)
            .SetUseOsDefaultSize(false)
            .SetResizable(true)
            .Center();

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            Console.Error.WriteLine($"[Photino Desktop Error] {error.ExceptionObject}");
        };

        app.Run();
    }
}
