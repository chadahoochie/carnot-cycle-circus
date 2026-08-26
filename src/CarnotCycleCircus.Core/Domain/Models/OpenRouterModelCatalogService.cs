using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Models;

public class OpenRouterModelCatalogService : IModelCatalogService
{
    private readonly IOpenRouterClient _openRouterClient;
    private readonly IPersistentStorageService _storage;
    private readonly IApiKeyVaultService _keyVault;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const string CacheFilePath = "data/openrouter-models-cache.json";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private ModelCatalogCache? _memoryCache;
    private HashSet<string>? _memoryFavorites;

    public OpenRouterModelCatalogService(
        IOpenRouterClient openRouterClient,
        IPersistentStorageService storage,
        IApiKeyVaultService keyVault,
        TimeProvider? timeProvider = null)
    {
        _openRouterClient = openRouterClient;
        _storage = storage;
        _keyVault = keyVault;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();

            // 1. Check in-memory cache if not forced and not expired
            if (!forceRefresh && _memoryCache != null && (now - _memoryCache.LastFetchedUtc) < DefaultTtl)
            {
                return ApplyFavorites(_memoryCache.Models, GetCurrentFavoriteIds());
            }

            // 2. Try loading from persistent storage if memory is empty and not forced
            if (!forceRefresh && _memoryCache == null)
            {
                var storedCache = await _storage.LoadJsonAsync<ModelCatalogCache>(CacheFilePath, cancellationToken);
                if (storedCache != null && storedCache.Models.Count > 0)
                {
                    _memoryFavorites = new HashSet<string>(storedCache.FavoriteModelIds, StringComparer.OrdinalIgnoreCase);
                    _memoryCache = storedCache;

                    if ((now - storedCache.LastFetchedUtc) < DefaultTtl)
                    {
                        return ApplyFavorites(_memoryCache.Models, _memoryFavorites);
                    }
                }
            }

            // 3. Fetch fresh data from OpenRouter (or fallback to defaults if offline/sandbox)
            var freshModels = await FetchAndClassifyModelsAsync(cancellationToken);
            var favorites = GetCurrentFavoriteIds();

            _memoryCache = new ModelCatalogCache(
                LastFetchedUtc: now,
                Models: freshModels,
                FavoriteModelIds: favorites.ToList()
            );

            await _storage.SaveJsonAsync(CacheFilePath, _memoryCache, cancellationToken);

            return ApplyFavorites(freshModels, favorites);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var models = await GetModelsAsync(false, cancellationToken);
        return models.Where(m => m.IsFavorite).ToList();
    }

    public async Task<bool> ToggleFavoriteAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var favorites = GetCurrentFavoriteIds();
            bool newFavoriteState;

            if (favorites.Contains(modelId))
            {
                favorites.Remove(modelId);
                newFavoriteState = false;
            }
            else
            {
                favorites.Add(modelId);
                newFavoriteState = true;
            }

            _memoryFavorites = favorites;

            if (_memoryCache != null)
            {
                var updatedModels = _memoryCache.Models
                    .Select(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)
                        ? m with { IsFavorite = newFavoriteState }
                        : m)
                    .ToList();

                _memoryCache = _memoryCache with
                {
                    Models = updatedModels,
                    FavoriteModelIds = favorites.ToList()
                };

                await _storage.SaveJsonAsync(CacheFilePath, _memoryCache, cancellationToken);
            }

            return newFavoriteState;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetFavoriteAsync(string modelId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var favorites = GetCurrentFavoriteIds();
            if (isFavorite)
            {
                favorites.Add(modelId);
            }
            else
            {
                favorites.Remove(modelId);
            }

            _memoryFavorites = favorites;

            if (_memoryCache != null)
            {
                var updatedModels = _memoryCache.Models
                    .Select(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)
                        ? m with { IsFavorite = isFavorite }
                        : m)
                    .ToList();

                _memoryCache = _memoryCache with
                {
                    Models = updatedModels,
                    FavoriteModelIds = favorites.ToList()
                };

                await _storage.SaveJsonAsync(CacheFilePath, _memoryCache, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetRecommendedModelsAsync(
        AgentRole role,
        CancellationToken cancellationToken = default)
    {
        var all = await GetModelsAsync(false, cancellationToken);

        return role switch
        {
            AgentRole.TechnicalProductManager => all.Where(m => m.StrengthAreas.Contains(ModelStrengthArea.GeneralOrchestration)).ToList(),
            AgentRole.LeadArchitect => all.Where(m => m.StrengthAreas.Contains(ModelStrengthArea.DeepReasoning) || m.StrengthAreas.Contains(ModelStrengthArea.GeneralOrchestration)).ToList(),
            AgentRole.SoftwareDeveloper => all.Where(m => m.StrengthAreas.Contains(ModelStrengthArea.CodeGeneration)).ToList(),
            AgentRole.SecurityEngineer => all.Where(m => m.StrengthAreas.Contains(ModelStrengthArea.SecurityAudit) || m.StrengthAreas.Contains(ModelStrengthArea.DeepReasoning)).ToList(),
            AgentRole.OptimizationEngineer => all.Where(m => m.StrengthAreas.Contains(ModelStrengthArea.CodeGeneration) || m.StrengthAreas.Contains(ModelStrengthArea.DeepReasoning)).ToList(),
            AgentRole.PrincipalQAAnalyst => all.Where(m => m.StrengthAreas.Contains(ModelStrengthArea.DeepReasoning) || m.StrengthAreas.Contains(ModelStrengthArea.CodeGeneration)).ToList(),
            _ => all
        };
    }

    public async Task<ModelCatalogEntry?> GetModelByIdAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        var models = await GetModelsAsync(false, cancellationToken);
        return models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ModelCatalogHealthStatus> GetCatalogStatusAsync(CancellationToken cancellationToken = default)
    {
        var models = await GetModelsAsync(false, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var lastFetched = _memoryCache?.LastFetchedUtc ?? default;
        var isStale = (now - lastFetched) >= DefaultTtl;

        return new ModelCatalogHealthStatus(
            TotalModels: models.Count,
            TotalFavorites: models.Count(m => m.IsFavorite),
            LastFetchedUtc: lastFetched,
            IsStale: isStale,
            Source: _memoryCache != null ? "OpenRouter Live Cache" : "Curated Fallback Catalog"
        );
    }

    private HashSet<string> GetCurrentFavoriteIds()
    {
        if (_memoryFavorites != null) return _memoryFavorites;

        if (_memoryCache?.FavoriteModelIds is { Count: > 0 } favs)
        {
            _memoryFavorites = new HashSet<string>(favs, StringComparer.OrdinalIgnoreCase);
            return _memoryFavorites;
        }

        _memoryFavorites = new HashSet<string>(GetDefaultFavoriteIds(), StringComparer.OrdinalIgnoreCase);
        return _memoryFavorites;
    }

    private static IReadOnlyList<ModelCatalogEntry> ApplyFavorites(
        IReadOnlyList<ModelCatalogEntry> models,
        ISet<string> favoriteIds)
    {
        return models
            .Select(m => m with { IsFavorite = favoriteIds.Contains(m.Id) })
            .ToList();
    }

    private async Task<IReadOnlyList<ModelCatalogEntry>> FetchAndClassifyModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var apiKey = _keyVault.GetActiveKey()?.RawApiKey;
            var rawModels = await _openRouterClient.FetchModelsAsync(apiKey, cancellationToken);

            if (rawModels != null && rawModels.Count > 0)
            {
                var now = _timeProvider.GetUtcNow();
                var catalog = new List<ModelCatalogEntry>(rawModels.Count);

                foreach (var raw in rawModels)
                {
                    if (string.IsNullOrWhiteSpace(raw.Id)) continue;

                    var promptPrice = raw.Pricing?.GetPromptDecimal() ?? 0m;
                    var completionPrice = raw.Pricing?.GetCompletionDecimal() ?? 0m;
                    var imagePrice = raw.Pricing?.GetImageDecimal();
                    var requestPrice = raw.Pricing?.GetRequestDecimal();

                    var pricing = new OpenRouterPricing(promptPrice, completionPrice, imagePrice, requestPrice);
                    var arch = new OpenRouterArchitecture(raw.Architecture?.Modality, raw.Architecture?.Tokenizer, raw.Architecture?.InstructType);

                    var costTier = DetermineCostTier(promptPrice, raw.Id);
                    var strengthAreas = ClassifyStrengthAreas(raw.Id, raw.Name ?? raw.Id, raw.Description ?? string.Empty, arch, costTier);

                    catalog.Add(new ModelCatalogEntry(
                        Id: raw.Id,
                        Name: !string.IsNullOrWhiteSpace(raw.Name) ? raw.Name : raw.Id,
                        Description: raw.Description ?? string.Empty,
                        ContextLength: raw.ContextLength ?? 128000,
                        Pricing: pricing,
                        Architecture: arch,
                        CostTier: costTier,
                        StrengthAreas: strengthAreas,
                        IsFavorite: false,
                        LastUpdatedUtc: now
                    ));
                }

                // Ensure essential curated models exist in the catalog even if filtered
                var defaultCatalog = GetDefaultCatalog();
                foreach (var def in defaultCatalog)
                {
                    if (!catalog.Any(c => string.Equals(c.Id, def.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        catalog.Add(def);
                    }
                }

                return catalog;
            }
        }
        catch
        {
            // Network failure or sandbox mode: fall back gracefully to default catalog
        }

        return GetDefaultCatalog();
    }

    public static ModelCostTier DetermineCostTier(decimal promptPerToken, string modelId)
    {
        var promptPerMillion = promptPerToken * 1_000_000m;
        if (promptPerMillion == 0m || modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase))
        {
            return ModelCostTier.Free;
        }
        if (promptPerMillion <= 1.00m)
        {
            return ModelCostTier.Budget;
        }
        if (promptPerMillion <= 5.00m)
        {
            return ModelCostTier.Standard;
        }
        return ModelCostTier.Premium;
    }

    public static IReadOnlyList<ModelStrengthArea> ClassifyStrengthAreas(
        string modelId,
        string name,
        string description,
        OpenRouterArchitecture architecture,
        ModelCostTier costTier)
    {
        var areas = new HashSet<ModelStrengthArea>();
        var lowerId = modelId.ToLowerInvariant();
        var lowerName = name.ToLowerInvariant();
        var lowerDesc = (description ?? string.Empty).ToLowerInvariant();

        // 1. Multimodal / Vision
        if (architecture.Modality?.Contains("image", StringComparison.OrdinalIgnoreCase) == true ||
            lowerId.Contains("vision") || lowerId.Contains("-vl") || lowerId.Contains("pixtral") ||
            lowerId.Contains("gpt-4o") || lowerId.Contains("claude-3") || lowerId.Contains("gemini"))
        {
            areas.Add(ModelStrengthArea.MultimodalVision);
        }

        // 2. Code Generation
        if (lowerId.Contains("coder") || lowerId.Contains("code") || lowerId.Contains("starcoder") ||
            lowerId.Contains("claude-3.7") || lowerId.Contains("claude-3.5-sonnet") || lowerId.Contains("gpt-4o") ||
            lowerId.Contains("deepseek-v3") || lowerId.Contains("deepseek-r1") || lowerId.Contains("qwen-2.5-coder") ||
            lowerDesc.Contains("code") || lowerDesc.Contains("programming") || lowerDesc.Contains("coding"))
        {
            areas.Add(ModelStrengthArea.CodeGeneration);
        }

        // 3. Deep Reasoning
        if (lowerId.Contains("r1") || lowerId.Contains("o1") || lowerId.Contains("o3") ||
            lowerId.Contains("reasoning") || lowerId.Contains("thinking") || lowerId.Contains("thought") ||
            lowerId.Contains("qwq") || lowerDesc.Contains("reasoning") || lowerDesc.Contains("chain of thought") ||
            lowerDesc.Contains("math") || lowerDesc.Contains("complex logic"))
        {
            areas.Add(ModelStrengthArea.DeepReasoning);
        }

        // 4. Low Latency / Fast Fallback
        if (lowerId.Contains("haiku") || lowerId.Contains("flash") || lowerId.Contains("mini") ||
            lowerId.Contains("8b") || lowerId.Contains("7b") || lowerId.Contains("3b") || lowerId.Contains("1b") ||
            lowerId.Contains("nano") || lowerId.Contains("small") || lowerId.Contains("turbo") ||
            costTier == ModelCostTier.Free || costTier == ModelCostTier.Budget)
        {
            areas.Add(ModelStrengthArea.LowLatencyFallback);
        }

        // 5. Security & Formal Audit
        if (lowerId.Contains("o3-mini") || lowerId.Contains("o1") || lowerId.Contains("claude-3.7") ||
            lowerId.Contains("claude-3.5-sonnet") || lowerId.Contains("guard") || lowerId.Contains("security") ||
            lowerDesc.Contains("security") || lowerDesc.Contains("audit") || lowerDesc.Contains("compliance"))
        {
            areas.Add(ModelStrengthArea.SecurityAudit);
        }

        // 6. General Orchestration
        if (lowerId.Contains("gpt-4o") || lowerId.Contains("claude-3.7") || lowerId.Contains("claude-3.5") ||
            lowerId.Contains("gemini-1.5-pro") || lowerId.Contains("gemini-2.0-pro") || lowerId.Contains("gemini-2.0-flash") ||
            lowerId.Contains("mistral-large") || lowerId.Contains("llama-3.3-70b") || lowerId.Contains("command-r") ||
            areas.Count == 0)
        {
            areas.Add(ModelStrengthArea.GeneralOrchestration);
        }

        return areas.ToList();
    }

    public static IReadOnlyList<string> GetDefaultFavoriteIds() =>
    [
        "anthropic/claude-3.7-sonnet",
        "openai/gpt-4o",
        "openai/o3-mini",
        "deepseek/deepseek-r1",
        "qwen/qwen-2.5-coder-32b-instruct",
        "anthropic/claude-3.5-haiku",
        "google/gemini-2.0-flash-001",
        "meta-llama/llama-3.3-70b-instruct:free"
    ];

    public static IReadOnlyList<ModelCatalogEntry> GetDefaultCatalog() =>
    [
        new(
            Id: "anthropic/claude-3.7-sonnet",
            Name: "Anthropic: Claude 3.7 Sonnet",
            Description: "Claude 3.7 Sonnet is Anthropic's flagship hybrid reasoning and coding powerhouse model.",
            ContextLength: 200000,
            Pricing: new OpenRouterPricing(0.000003m, 0.000015m),
            Architecture: new OpenRouterArchitecture("text+image->text", "claude"),
            CostTier: ModelCostTier.Standard,
            StrengthAreas: [ModelStrengthArea.CodeGeneration, ModelStrengthArea.DeepReasoning, ModelStrengthArea.GeneralOrchestration, ModelStrengthArea.MultimodalVision, ModelStrengthArea.SecurityAudit],
            IsFavorite: true
        ),
        new(
            Id: "openai/gpt-4o",
            Name: "OpenAI: GPT-4o",
            Description: "OpenAI's flagship omni-model with exceptional general orchestration, vision, and tool calling.",
            ContextLength: 128000,
            Pricing: new OpenRouterPricing(0.0000025m, 0.00001m),
            Architecture: new OpenRouterArchitecture("text+image->text", "gpt4"),
            CostTier: ModelCostTier.Standard,
            StrengthAreas: [ModelStrengthArea.GeneralOrchestration, ModelStrengthArea.CodeGeneration, ModelStrengthArea.MultimodalVision],
            IsFavorite: true
        ),
        new(
            Id: "openai/o3-mini",
            Name: "OpenAI: o3-mini",
            Description: "High-intelligence lightweight reasoning model specialized for math, coding, and security audit.",
            ContextLength: 200000,
            Pricing: new OpenRouterPricing(0.0000011m, 0.0000044m),
            Architecture: new OpenRouterArchitecture("text->text", "gpt4"),
            CostTier: ModelCostTier.Standard,
            StrengthAreas: [ModelStrengthArea.DeepReasoning, ModelStrengthArea.CodeGeneration, ModelStrengthArea.SecurityAudit],
            IsFavorite: true
        ),
        new(
            Id: "deepseek/deepseek-r1",
            Name: "DeepSeek: DeepSeek R1",
            Description: "Open-weights frontier reasoning model with exceptional chain-of-thought verification.",
            ContextLength: 128000,
            Pricing: new OpenRouterPricing(0.00000055m, 0.00000219m),
            Architecture: new OpenRouterArchitecture("text->text", "deepseek"),
            CostTier: ModelCostTier.Budget,
            StrengthAreas: [ModelStrengthArea.DeepReasoning, ModelStrengthArea.CodeGeneration],
            IsFavorite: true
        ),
        new(
            Id: "qwen/qwen-2.5-coder-32b-instruct",
            Name: "Qwen: Qwen 2.5 Coder 32B Instruct",
            Description: "Top-tier code generation, syntax optimization, and bug fixing LLM from Alibaba.",
            ContextLength: 128000,
            Pricing: new OpenRouterPricing(0.00000018m, 0.00000018m),
            Architecture: new OpenRouterArchitecture("text->text", "qwen"),
            CostTier: ModelCostTier.Budget,
            StrengthAreas: [ModelStrengthArea.CodeGeneration],
            IsFavorite: true
        ),
        new(
            Id: "anthropic/claude-3.5-haiku",
            Name: "Anthropic: Claude 3.5 Haiku",
            Description: "Ultra-fast, low-latency model for rapid failover, streaming execution, and light tasks.",
            ContextLength: 200000,
            Pricing: new OpenRouterPricing(0.0000008m, 0.000004m),
            Architecture: new OpenRouterArchitecture("text+image->text", "claude"),
            CostTier: ModelCostTier.Budget,
            StrengthAreas: [ModelStrengthArea.LowLatencyFallback, ModelStrengthArea.GeneralOrchestration],
            IsFavorite: true
        ),
        new(
            Id: "google/gemini-2.0-flash-001",
            Name: "Google: Gemini 2.0 Flash",
            Description: "Next-gen multimodal workhorse with lightning-fast latency and large context window.",
            ContextLength: 1048576,
            Pricing: new OpenRouterPricing(0.0000001m, 0.0000004m),
            Architecture: new OpenRouterArchitecture("text+image->text", "gemini"),
            CostTier: ModelCostTier.Budget,
            StrengthAreas: [ModelStrengthArea.LowLatencyFallback, ModelStrengthArea.MultimodalVision, ModelStrengthArea.GeneralOrchestration],
            IsFavorite: true
        ),
        new(
            Id: "meta-llama/llama-3.3-70b-instruct:free",
            Name: "Meta: Llama 3.3 70B Instruct (Free)",
            Description: "Free tier state-of-the-art open weights model with 70B parameters.",
            ContextLength: 131072,
            Pricing: new OpenRouterPricing(0m, 0m),
            Architecture: new OpenRouterArchitecture("text->text", "llama"),
            CostTier: ModelCostTier.Free,
            StrengthAreas: [ModelStrengthArea.GeneralOrchestration, ModelStrengthArea.LowLatencyFallback, ModelStrengthArea.CodeGeneration],
            IsFavorite: true
        ),
        new(
            Id: "meta-llama/llama-3.3-70b-instruct",
            Name: "Meta: Llama 3.3 70B Instruct",
            Description: "Meta's flagship 70B parameter open weights foundation model.",
            ContextLength: 131072,
            Pricing: new OpenRouterPricing(0.00000012m, 0.0000003m),
            Architecture: new OpenRouterArchitecture("text->text", "llama"),
            CostTier: ModelCostTier.Budget,
            StrengthAreas: [ModelStrengthArea.GeneralOrchestration, ModelStrengthArea.CodeGeneration],
            IsFavorite: false
        ),
        new(
            Id: "deepseek/deepseek-chat",
            Name: "DeepSeek: DeepSeek V3",
            Description: "MoE foundation model with 671B parameters and stellar general benchmark scores.",
            ContextLength: 128000,
            Pricing: new OpenRouterPricing(0.00000014m, 0.00000028m),
            Architecture: new OpenRouterArchitecture("text->text", "deepseek"),
            CostTier: ModelCostTier.Budget,
            StrengthAreas: [ModelStrengthArea.GeneralOrchestration, ModelStrengthArea.CodeGeneration],
            IsFavorite: false
        ),
        new(
            Id: "mistralai/mistral-large-2407",
            Name: "Mistral: Mistral Large 2",
            Description: "Flagship European enterprise multilingual model with advanced reasoning and 128k context.",
            ContextLength: 128000,
            Pricing: new OpenRouterPricing(0.000002m, 0.000006m),
            Architecture: new OpenRouterArchitecture("text->text", "mistral"),
            CostTier: ModelCostTier.Standard,
            StrengthAreas: [ModelStrengthArea.GeneralOrchestration, ModelStrengthArea.CodeGeneration],
            IsFavorite: false
        )
    ];
}
