using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Teams;

namespace CarnotCycleCircus.Tests;

public sealed class MockOpenRouterClient : IOpenRouterClient
{
    public OpenRouterChatRequest? LastRequest { get; private set; }
    public string? LastApiKey { get; private set; }
    public bool ShouldThrow { get; set; }
    public Func<OpenRouterChatRequest, string>? ResponseFactory { get; set; }
    public Func<OpenRouterChatRequest, OpenRouterChatResponse>? FullResponseFactory { get; set; }

    public Task<OpenRouterChatResponse> CompleteAsync(
        OpenRouterChatRequest request,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldThrow)
        {
            throw new HttpRequestException("OpenRouter API returned 401 Unauthorized: Invalid API key.");
        }

        LastRequest = request;
        LastApiKey = apiKey;

        if (FullResponseFactory != null)
        {
            return Task.FromResult(FullResponseFactory(request));
        }

        var content = ResponseFactory != null
            ? ResponseFactory(request)
            : GetDefaultResponseForPrompt(request);

        var response = new OpenRouterChatResponse(
            Id: "gen-mock-test",
            Model: request.Model,
            Choices: [
                new OpenRouterChoice(0, new OpenRouterMessage("assistant", content), "stop")
            ],
            Usage: new OpenRouterUsage(200, 150, 350)
        );

        return Task.FromResult(response);
    }

    public Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<OpenRouterRawModelDto> list = Array.Empty<OpenRouterRawModelDto>();
        return Task.FromResult(list);
    }

    private static string GetDefaultResponseForPrompt(OpenRouterChatRequest request)
    {
        var sys = request.Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
        var user = request.Messages.FirstOrDefault(m => m.Role == "user")?.Content ?? "";

        if (user.Contains("Requirements Research", StringComparison.OrdinalIgnoreCase))
        {
            return "# Requirements Research Brief\n## 1. Domain Context\nFeasibility verified.";
        }
        if (user.Contains("Product Requirements Document", StringComparison.OrdinalIgnoreCase))
        {
            return "# Product Requirements Document\n## 1. Objectives\n- [ ] AC1: Completed.";
        }
        if (user.Contains("Architectural Decision Record", StringComparison.OrdinalIgnoreCase))
        {
            return "# ADR-014: Architecture\n## Status\nAccepted\n```csharp:Contracts/IService.cs\nnamespace CarnotCycleCircus.Domain;\npublic interface IService { }\n```";
        }
        if (user.Contains("multi-file implementation", StringComparison.OrdinalIgnoreCase) || sys.Contains("Developer", StringComparison.OrdinalIgnoreCase))
        {
            return "```csharp:Services/Service.cs\nnamespace CarnotCycleCircus.Services;\nusing System;\nusing System.Threading;\nusing System.Threading.Tasks;\npublic sealed class Service\n{\n    public async ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken = default)\n    {\n        await Task.Yield();\n        return true;\n    }\n}\n```";
        }
        if (user.Contains("STRIDE", StringComparison.OrdinalIgnoreCase))
        {
            return "# STRIDE Threat Model Audit\n## Status\nAPPROVED - 0 Critical, 0 High vulnerabilities.";
        }
        if (user.Contains("Benchmark", StringComparison.OrdinalIgnoreCase))
        {
            return "# Performance & Allocation Benchmark Report\n## SLA\n0 B Gen0 heap allocations verified.";
        }
        if (user.Contains("QA", StringComparison.OrdinalIgnoreCase) || user.Contains("Scorecard", StringComparison.OrdinalIgnoreCase))
        {
            return "# QA Certification & Acceptance Scorecard\n## Decision\nCertification Status: PASSED.";
        }

        return "# Release Manifest\n## 1. Solution Package\nStatus: PACKAGED & PRODUCTION READY.";
    }
}

public sealed class StaticInferenceResolver : IAgentInferenceResolver
{
    private readonly string _model;
    private readonly string _apiKey;
    private readonly string? _fallbackModel;

    public StaticInferenceResolver(string model = "deepseek/deepseek-chat", string apiKey = "sk-or-v1-test-active-key", string? fallbackModel = null)
    {
        _model = model;
        _apiKey = apiKey;
        _fallbackModel = fallbackModel;
    }

    public (string Model, string ApiKey) ResolveInferenceParameters(AgentMember member, EngineeringTeam team)
    {
        return (_model, _apiKey);
    }

    public ResolvedInferenceConfig ResolveInferenceConfig(AgentMember member, EngineeringTeam team)
    {
        return new ResolvedInferenceConfig(_model, _fallbackModel, _apiKey);
    }
}
