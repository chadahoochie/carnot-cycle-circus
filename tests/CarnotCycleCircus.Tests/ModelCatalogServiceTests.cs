using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Models;
using CarnotCycleCircus.Core.Domain.Storage;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ModelCatalogServiceTests
{
    private sealed class MockStorageService : IPersistentStorageService
    {
        public Dictionary<string, string> Files { get; } = new();
        public CarnotStorageOptions Options { get; } = new();

        public Task SaveJsonAsync<T>(string relativePath, T data, CancellationToken cancellationToken = default)
        {
            Files[relativePath] = JsonSerializer.Serialize(data);
            return Task.CompletedTask;
        }

        public Task<T?> LoadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
        {
            if (Files.TryGetValue(relativePath, out var json))
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(json));
            }
            return Task.FromResult<T?>(default);
        }

        public Task SaveTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
        {
            Files[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<string?> LoadTextAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Files.TryGetValue(relativePath, out var content);
            return Task.FromResult(content);
        }

        public Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Files.ContainsKey(relativePath));
        }

        public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Files.Remove(relativePath));
        }

        public Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory = "", string searchPattern = "*.*", CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> list = Files.Keys.ToList();
            return Task.FromResult(list);
        }

        public Task<StorageHealthReport> GetStorageHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StorageHealthReport(true, "mock", 0, Files.Count, []));
        }
    }

    private sealed class MockOpenRouterClient : IOpenRouterClient
    {
        public IReadOnlyList<OpenRouterRawModelDto> ModelsToReturn { get; set; } = [];
        public bool ShouldThrow { get; set; }

        public Task<OpenRouterChatResponse> CompleteAsync(OpenRouterChatRequest request, string apiKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OpenRouterChatResponse("id", request.Model, [], null));
        }

        public Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(string? apiKey = null, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow) throw new HttpRequestException("Network failure");
            return Task.FromResult(ModelsToReturn);
        }
    }

    [Theory]
    [InlineData(0.000000, "meta-llama/llama-3.3-70b-instruct:free", ModelCostTier.Free)]
    [InlineData(0.00000014, "deepseek/deepseek-chat", ModelCostTier.Budget)] // $0.14/M
    [InlineData(0.0000008, "anthropic/claude-3.5-haiku", ModelCostTier.Budget)] // $0.80/M
    [InlineData(0.0000025, "openai/gpt-4o", ModelCostTier.Standard)] // $2.50/M
    [InlineData(0.000003, "anthropic/claude-3.7-sonnet", ModelCostTier.Standard)] // $3.00/M
    [InlineData(0.000015, "openai/o1", ModelCostTier.Premium)] // $15.00/M
    public void DetermineCostTier_ShouldClassifyPricingAccurately(decimal promptPrice, string modelId, ModelCostTier expectedTier)
    {
        var tier = OpenRouterModelCatalogService.DetermineCostTier(promptPrice, modelId);
        tier.Should().Be(expectedTier);
    }

    [Fact]
    public void ClassifyStrengthAreas_ShouldDetectAppropriateStrengths()
    {
        var coderAreas = OpenRouterModelCatalogService.ClassifyStrengthAreas(
            "qwen/qwen-2.5-coder-32b-instruct",
            "Qwen 2.5 Coder",
            "State-of-the-art code generation model",
            new OpenRouterArchitecture("text->text"),
            ModelCostTier.Budget);

        coderAreas.Should().Contain(ModelStrengthArea.CodeGeneration);

        var reasoningAreas = OpenRouterModelCatalogService.ClassifyStrengthAreas(
            "deepseek/deepseek-r1",
            "DeepSeek R1",
            "Frontier chain-of-thought reasoning",
            new OpenRouterArchitecture("text->text"),
            ModelCostTier.Budget);

        reasoningAreas.Should().Contain(ModelStrengthArea.DeepReasoning);

        var visionAreas = OpenRouterModelCatalogService.ClassifyStrengthAreas(
            "google/gemini-2.0-flash-001",
            "Gemini 2.0 Flash",
            "Multimodal high-speed engine",
            new OpenRouterArchitecture("text+image->text"),
            ModelCostTier.Budget);

        visionAreas.Should().Contain(ModelStrengthArea.MultimodalVision);
        visionAreas.Should().Contain(ModelStrengthArea.LowLatencyFallback);
    }

    [Fact]
    public async Task GetModelsAsync_WhenOfflineOrNoClientData_ShouldReturnCuratedDefaultCatalog()
    {
        var storage = new MockStorageService();
        var client = new MockOpenRouterClient { ModelsToReturn = [] };
        var vault = new ApiKeyVaultService();

        var service = new OpenRouterModelCatalogService(client, storage, vault);

        var models = await service.GetModelsAsync();

        models.Should().NotBeEmpty();
        models.Should().Contain(m => m.Id == "anthropic/claude-3.7-sonnet");
        models.Should().Contain(m => m.Id == "openai/gpt-4o");
        models.Should().Contain(m => m.Id == "deepseek/deepseek-r1");
        models.Should().Contain(m => m.Id == "qwen/qwen-2.5-coder-32b-instruct");

        var sonnet = models.First(m => m.Id == "anthropic/claude-3.7-sonnet");
        sonnet.IsFavorite.Should().BeTrue();
        sonnet.SupportsVision.Should().BeTrue();
        sonnet.CostTier.Should().Be(ModelCostTier.Standard);
    }

    [Fact]
    public async Task GetModelsAsync_WithLiveClient_ShouldParseAndCacheToPersistentStorage()
    {
        var storage = new MockStorageService();
        var client = new MockOpenRouterClient
        {
            ModelsToReturn =
            [
                new OpenRouterRawModelDto(
                    Id: "custom/super-coder-99b",
                    Name: "Custom Super Coder 99B",
                    Description: "Specialized model for low-level C# optimization and zero-alloc spans.",
                    ContextLength: 64000,
                    Pricing: new OpenRouterRawPricingDto(
                        Prompt: JsonDocument.Parse("\"0.0000005\"").RootElement,
                        Completion: JsonDocument.Parse("\"0.000001\"").RootElement
                    ),
                    Architecture: new OpenRouterRawArchitectureDto("text->text")
                )
            ]
        };
        var vault = new ApiKeyVaultService();

        var service = new OpenRouterModelCatalogService(client, storage, vault);

        var models = await service.GetModelsAsync();

        models.Should().Contain(m => m.Id == "custom/super-coder-99b");
        var custom = models.First(m => m.Id == "custom/super-coder-99b");
        custom.CostTier.Should().Be(ModelCostTier.Budget);
        custom.StrengthAreas.Should().Contain(ModelStrengthArea.CodeGeneration);
        custom.PromptPerMillion.Should().Be(0.50m);
        custom.CompletionPerMillion.Should().Be(1.00m);

        // Verify storage file was saved
        storage.Files.Should().ContainKey("data/openrouter-models-cache.json");
    }

    [Fact]
    public async Task ToggleFavoriteAsync_ShouldToggleStateAndPersistAcrossLoads()
    {
        var storage = new MockStorageService();
        var client = new MockOpenRouterClient();
        var vault = new ApiKeyVaultService();

        var service = new OpenRouterModelCatalogService(client, storage, vault);

        // Initial default favorites include claude-3.7-sonnet
        var isFav = (await service.GetFavoritesAsync()).Any(m => m.Id == "anthropic/claude-3.7-sonnet");
        isFav.Should().BeTrue();

        // Toggle favorite off
        var toggled = await service.ToggleFavoriteAsync("anthropic/claude-3.7-sonnet");
        toggled.Should().BeFalse();

        var favoritesAfter = await service.GetFavoritesAsync();
        favoritesAfter.Should().NotContain(m => m.Id == "anthropic/claude-3.7-sonnet");

        // Toggle favorite back on
        var toggledOn = await service.ToggleFavoriteAsync("anthropic/claude-3.7-sonnet");
        toggledOn.Should().BeTrue();

        var favoritesFinal = await service.GetFavoritesAsync();
        favoritesFinal.Should().Contain(m => m.Id == "anthropic/claude-3.7-sonnet");
    }

    [Fact]
    public async Task GetRecommendedModelsAsync_ShouldFilterByAgentRoleSpecialization()
    {
        var storage = new MockStorageService();
        var client = new MockOpenRouterClient();
        var vault = new ApiKeyVaultService();

        var service = new OpenRouterModelCatalogService(client, storage, vault);

        var devModels = await service.GetRecommendedModelsAsync(AgentRole.SoftwareDeveloper);
        devModels.Should().OnlyContain(m => m.StrengthAreas.Contains(ModelStrengthArea.CodeGeneration));
        devModels.Should().Contain(m => m.Id == "qwen/qwen-2.5-coder-32b-instruct");

        var qaModels = await service.GetRecommendedModelsAsync(AgentRole.PrincipalQAAnalyst);
        qaModels.Should().Contain(m => m.Id == "deepseek/deepseek-r1");

        var secModels = await service.GetRecommendedModelsAsync(AgentRole.SecurityEngineer);
        secModels.Should().Contain(m => m.Id == "openai/o3-mini");
    }

    [Fact]
    public async Task GetCatalogStatusAsync_ShouldReturnHealthyMetrics()
    {
        var storage = new MockStorageService();
        var client = new MockOpenRouterClient();
        var vault = new ApiKeyVaultService();

        var service = new OpenRouterModelCatalogService(client, storage, vault);

        var status = await service.GetCatalogStatusAsync();
        status.TotalModels.Should().BeGreaterThan(5);
        status.TotalFavorites.Should().BeGreaterThan(0);
        status.IsStale.Should().BeFalse();
    }
}
