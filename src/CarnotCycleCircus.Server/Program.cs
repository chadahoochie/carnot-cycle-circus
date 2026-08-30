using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Blueprints;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Harvester;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Learning;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Models;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Extensions;
using CarnotCycleCircus.Server.Hubs;
using CarnotCycleCircus.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Carnot Core Domain & Storage Engine
builder.Services.AddCarnotCycleCircusCore();

// Add SignalR Real-Time Streaming & Event Bridge
builder.Services.AddSignalR();
builder.Services.AddHostedService<SignalREventBridge>();

// Add CORS for Desktop / Web Client access
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

app.UseCors();

// -----------------------------------------------------------------------------
// Health & Diagnostic Endpoints
// -----------------------------------------------------------------------------
app.MapGet("/health", async (IPersistentStorageService storage) =>
{
    var health = await storage.GetStorageHealthAsync();
    return Results.Ok(new
    {
        status = health.IsHealthy ? "Healthy" : "Degraded",
        timestamp = DateTimeOffset.UtcNow,
        role = "Carnot Agent Host Server",
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

// -----------------------------------------------------------------------------
// Tickets & Work Decomposition Endpoints
// -----------------------------------------------------------------------------
app.MapGet("/api/tickets", (ITicketStore ticketStore) =>
{
    return Results.Ok(ticketStore.GetAllTickets());
});

app.MapPost("/api/tickets/create", (ITicketStore ticketStore, TicketItem ticket) =>
{
    var created = ticketStore.CreateTicket(ticket);
    return Results.Created($"/api/tickets/{created.Id}", created);
});

app.MapPut("/api/tickets/{id}/status", (ITicketStore ticketStore, string id, TicketStatus status) =>
{
    var existing = ticketStore.GetTicketById(id);
    if (existing == null) return Results.NotFound();
    ticketStore.UpdateTicket(existing.WithStatus(status));
    return Results.Ok();
});

// -----------------------------------------------------------------------------
// Codebase Harvester & Directory Inspection Endpoints
// -----------------------------------------------------------------------------
app.MapGet("/api/harvester/inspect", (ICodebaseHarvesterService harvester, string? path) =>
{
    var result = harvester.InspectDirectory(path ?? "");
    return Results.Ok(result);
});

app.MapPost("/api/harvester/harvest", async (ICodebaseHarvesterService harvester, HarvestRequest request) =>
{
    var report = await harvester.HarvestDirectoryAsync(request.DirectoryPath, request.AutoGenerateBacklog);
    return Results.Ok(report);
});

// -----------------------------------------------------------------------------
// Project Ignition & Blueprint Endpoints
// -----------------------------------------------------------------------------
app.MapGet("/api/blueprints/suggestions", (IProjectBlueprintService blueprintService) =>
{
    return Results.Ok(blueprintService.GetSuggestedInitiatives());
});

app.MapPost("/api/blueprints/ignite", async (IProjectBlueprintService blueprintService, ProjectIgnitionRequest request) =>
{
    var result = await blueprintService.LaunchProjectAsync(request);
    return Results.Ok(result);
});

// -----------------------------------------------------------------------------
// Model Catalog & Key Vault Endpoints
// -----------------------------------------------------------------------------
app.MapGet("/api/models", async (IModelCatalogService catalogService) =>
{
    var models = await catalogService.GetModelsAsync();
    return Results.Ok(models);
});

app.MapGet("/api/keys/status", (IApiKeyVaultService vault) =>
{
    var status = vault.GetSecurityStatus();
    return Results.Ok(status);
});

app.MapPost("/api/keys/store", (IApiKeyVaultService vault, StoreKeyRequest request) =>
{
    var entry = vault.AddOrUpdateKey(request.KeyName, request.ApiKey, isActive: true);
    return Results.Ok(new { message = "Key securely stored and envelope-encrypted.", keyId = entry.KeyId });
});

// -----------------------------------------------------------------------------
// Continuous Learning & Self-Improvement
// -----------------------------------------------------------------------------
app.MapPost("/api/self-improvement/run", async (ISelfImprovementEngine engine) =>
{
    var report = await engine.RunSelfImprovementCycleAsync();
    return Results.Ok(report);
});

// -----------------------------------------------------------------------------
// SignalR Agent Telemetry Stream Hub
// -----------------------------------------------------------------------------
app.MapHub<AgentStreamHub>("/hubs/agent-stream");

app.Run();

public record HarvestRequest(string DirectoryPath, bool AutoGenerateBacklog = true);
public record StoreKeyRequest(string KeyName, string ApiKey);
