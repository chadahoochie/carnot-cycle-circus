using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Memory;

public record MemorySearchResult(
    MemoryEntry Entry,
    float SimilarityScore
);

public interface IPersistentMemoryStore
{
    Task StoreAsync(MemoryEntry entry, CancellationToken cancellationToken = default);
    Task<MemoryEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntry>> GetByTypeAsync(MemoryType type, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntry>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string query, int topK = 5, MemoryType? typeFilter = null, AgentRole? roleFilter = null, CancellationToken cancellationToken = default);
    Task<int> PruneAsync(float minImportanceThreshold, TimeSpan olderThan, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<float> GenerateEmbedding(string text);
}

public class EmbeddedVectorMemoryStore : IPersistentMemoryStore
{
    private readonly ConcurrentDictionary<string, MemoryEntry> _store = new(StringComparer.OrdinalIgnoreCase);
    private const int EmbeddingDimensions = 64;

    public Task StoreAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        var effectiveEntry = entry;
        if (entry.Embedding == null || entry.Embedding.Count == 0)
        {
            effectiveEntry = entry with { Embedding = GenerateEmbedding(entry.Content) };
        }

        _store[effectiveEntry.Id] = effectiveEntry;
        return Task.CompletedTask;
    }

    public Task<MemoryEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(id, out var entry))
        {
            var touched = entry.Touch();
            _store[id] = touched;
            return Task.FromResult<MemoryEntry?>(touched);
        }
        return Task.FromResult<MemoryEntry?>(null);
    }

    public Task<IReadOnlyList<MemoryEntry>> GetByTypeAsync(MemoryType type, CancellationToken cancellationToken = default)
    {
        var list = _store.Values.Where(e => e.Type == type).OrderByDescending(e => e.Timestamp).ToList();
        return Task.FromResult<IReadOnlyList<MemoryEntry>>(list);
    }

    public Task<IReadOnlyList<MemoryEntry>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default)
    {
        var list = _store.Values.Where(e => e.Role == role).OrderByDescending(e => e.Timestamp).ToList();
        return Task.FromResult<IReadOnlyList<MemoryEntry>>(list);
    }

    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        int topK = 5,
        MemoryType? typeFilter = null,
        AgentRole? roleFilter = null,
        CancellationToken cancellationToken = default)
    {
        var queryVector = GenerateEmbedding(query);
        var queryTokens = query.ToLowerInvariant().Split([' ', ',', '.', ';', ':', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

        var candidates = _store.Values.AsEnumerable();

        if (typeFilter.HasValue)
        {
            candidates = candidates.Where(e => e.Type == typeFilter.Value);
        }

        if (roleFilter.HasValue)
        {
            candidates = candidates.Where(e => e.Role == roleFilter.Value);
        }

        var results = new List<MemorySearchResult>();

        foreach (var entry in candidates)
        {
            var vectorSim = ComputeCosineSimilarity(queryVector, entry.Embedding);

            // Token overlap boost
            var entryText = entry.Content.ToLowerInvariant();
            var matchedTokens = queryTokens.Count(token => entryText.Contains(token));
            var tokenScore = queryTokens.Length > 0 ? (float)matchedTokens / queryTokens.Length : 0f;

            // Composite score combining vector similarity, token matching, and importance weight
            var finalScore = (vectorSim * 0.6f) + (tokenScore * 0.3f) + (entry.Importance * 0.1f);

            results.Add(new MemorySearchResult(entry.Touch(), finalScore));
        }

        var topResults = results
            .OrderByDescending(r => r.SimilarityScore)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemorySearchResult>>(topResults);
    }

    public Task<int> PruneAsync(float minImportanceThreshold, TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var toRemove = _store.Values
            .Where(e => e.Importance < minImportanceThreshold && e.LastAccessedAt < cutoff && e.Type is MemoryType.Working or MemoryType.Episodic)
            .Select(e => e.Id)
            .ToList();

        var removedCount = 0;
        foreach (var id in toRemove)
        {
            if (_store.TryRemove(id, out _))
            {
                removedCount++;
            }
        }

        return Task.FromResult(removedCount);
    }

    public Task<IReadOnlyList<MemoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = _store.Values.OrderByDescending(e => e.Timestamp).ToList();
        return Task.FromResult<IReadOnlyList<MemoryEntry>>(list);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<float> GenerateEmbedding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[EmbeddingDimensions];
        }

        var vector = new float[EmbeddingDimensions];
        var words = text.ToLowerInvariant().Split([' ', '\t', '\r', '\n', '.', ',', ';', '(', ')', '{', '}', '[', ']'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(word));
            for (int i = 0; i < EmbeddingDimensions; i++)
            {
                // Project byte pairs into dimension weights
                sbyte val = (sbyte)hashBytes[i % hashBytes.Length];
                vector[i] += val / 128f;
            }
        }

        // Normalize vector to unit length
        var sumSquares = 0f;
        for (int i = 0; i < EmbeddingDimensions; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        var magnitude = MathF.Sqrt(sumSquares);
        if (magnitude > 1e-6f)
        {
            for (int i = 0; i < EmbeddingDimensions; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }

    private static float ComputeCosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0) return 0f;

        var len = Math.Min(a.Count, b.Count);
        var dot = 0f;
        var magA = 0f;
        var magB = 0f;

        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom > 1e-6f ? dot / denom : 0f;
    }
}
