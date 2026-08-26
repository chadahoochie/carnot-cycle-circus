using System.Globalization;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Models;

public enum ModelStrengthArea
{
    GeneralOrchestration,
    CodeGeneration,
    DeepReasoning,
    LowLatencyFallback,
    SecurityAudit,
    MultimodalVision
}

public enum ModelCostTier
{
    Free,       // $0.00 / 1M prompt
    Budget,     // <= $1.00 / 1M prompt
    Standard,   // $1.01 - $5.00 / 1M prompt
    Premium     // > $5.00 / 1M prompt
}

public record OpenRouterPricing(
    decimal PromptPerToken,
    decimal CompletionPerToken,
    decimal? Image = null,
    decimal? Request = null
)
{
    public decimal PromptPerMillion => PromptPerToken * 1_000_000m;
    public decimal CompletionPerMillion => CompletionPerToken * 1_000_000m;

    public static decimal ParseDecimalSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return 0m;
    }
}

public record OpenRouterArchitecture(
    string? Modality = null,
    string? Tokenizer = null,
    string? InstructType = null
);

public record ModelCatalogEntry(
    string Id,
    string Name,
    string Description,
    int ContextLength,
    OpenRouterPricing Pricing,
    OpenRouterArchitecture Architecture,
    ModelCostTier CostTier,
    IReadOnlyList<ModelStrengthArea> StrengthAreas,
    bool IsFavorite = false,
    DateTimeOffset LastUpdatedUtc = default
)
{
    public bool SupportsVision => Architecture.Modality?.Contains("image", StringComparison.OrdinalIgnoreCase) == true;
    public decimal PromptPerMillion => Pricing.PromptPerMillion;
    public decimal CompletionPerMillion => Pricing.CompletionPerMillion;

    public string Provider => ExtractProvider(Id);

    public string FormattedPricing => (PromptPerMillion == 0m && CompletionPerMillion == 0m)
        ? "Free"
        : $"${PromptPerMillion:0.00} / ${CompletionPerMillion:0.00} per 1M";

    private static string ExtractProvider(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return "Unknown";
        var slashIdx = modelId.IndexOf('/');
        if (slashIdx > 0)
        {
            var p = modelId[..slashIdx].ToLowerInvariant();
            return p switch
            {
                "anthropic" => "Anthropic",
                "openai" => "OpenAI",
                "google" => "Google",
                "deepseek" => "DeepSeek",
                "meta-llama" or "meta" => "Meta",
                "qwen" or "alibaba" => "Qwen",
                "mistralai" or "mistral" => "Mistral",
                "cohere" => "Cohere",
                "microsoft" => "Microsoft",
                "amazon" or "nova" => "Amazon",
                _ => char.ToUpperInvariant(p[0]) + p[1..]
            };
        }
        return "Generic";
    }
}

public record ModelCatalogCache(
    DateTimeOffset LastFetchedUtc,
    IReadOnlyList<ModelCatalogEntry> Models,
    IReadOnlyList<string> FavoriteModelIds
);

public record ModelCatalogHealthStatus(
    int TotalModels,
    int TotalFavorites,
    DateTimeOffset LastFetchedUtc,
    bool IsStale,
    string Source
);
