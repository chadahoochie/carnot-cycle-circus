namespace CarnotCycleCircus.Core.Domain.Storage;

public record StorageFileEntry(
    string RelativePath,
    long SizeInBytes,
    DateTimeOffset LastModified
);

public record StorageHealthReport(
    bool IsHealthy,
    string RootDirectory,
    long TotalSizeBytes,
    int TotalFilesCount,
    IReadOnlyList<StorageFileEntry> Files,
    string? ErrorMessage = null
);

public interface IPersistentStorageService
{
    CarnotStorageOptions Options { get; }
    Task SaveJsonAsync<T>(string relativePath, T data, CancellationToken cancellationToken = default);
    Task<T?> LoadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default);
    Task SaveTextAsync(string relativePath, string content, CancellationToken cancellationToken = default);
    Task<string?> LoadTextAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory = "", string searchPattern = "*.*", CancellationToken cancellationToken = default);
    Task<StorageHealthReport> GetStorageHealthAsync(CancellationToken cancellationToken = default);
}
