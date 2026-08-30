using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Extensions;
using CarnotCycleCircus.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add Razor components with Interactive Server mode
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Carnot Cycle Circus Core Engine & Domain Services
builder.Services.AddCarnotCycleCircusCore();
builder.Services.AddSingleton<CarnotCycleCircus.UI.Services.INativeFolderPicker, CarnotCycleCircus.UI.Services.DefaultNativeFolderPicker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.UseStaticFiles();

// Container health check endpoints
app.MapGet("/health", async (IPersistentStorageService storage) =>
{
    var health = await storage.GetStorageHealthAsync();
    return Results.Ok(new
    {
        status = health.IsHealthy ? "Healthy" : "Degraded",
        timestamp = DateTimeOffset.UtcNow,
        entropyEfficiency = "99.9%",
        storage = new
        {
            directory = health.RootDirectory,
            totalFiles = health.TotalFilesCount,
            totalSizeBytes = health.TotalSizeBytes,
            isHealthy = health.IsHealthy
        }
    });
});

app.MapGet("/api/storage/health", async (IPersistentStorageService storage) =>
{
    var health = await storage.GetStorageHealthAsync();
    return Results.Ok(health);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
