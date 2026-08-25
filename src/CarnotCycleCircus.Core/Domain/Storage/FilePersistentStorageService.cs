using System.Text.Json;

namespace CarnotCycleCircus.Core.Domain.Storage;

public class FilePersistentStorageService : IPersistentStorageService
{
    private readonly CarnotStorageOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CarnotStorageOptions Options => _options;

    public FilePersistentStorageService(CarnotStorageOptions? options = null)
    {
        _options = options ?? new CarnotStorageOptions();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        EnsureDirectoriesCreated();
    }

    private void EnsureDirectoriesCreated()
    {
        try
        {
            Directory.CreateDirectory(_options.DataDirectory);
            Directory.CreateDirectory(_options.ArtifactsDirectory);
            Directory.CreateDirectory(_options.SkillsDirectory);
            Directory.CreateDirectory(_options.AdrsDirectory);
        }
        catch
        {
            // Ignore startup directory creation errors if constrained; will retry upon file write
        }
    }

    private string GetFullPath(string relativePath)
    {
        // Sanitize path to prevent directory traversal
        var cleanPath = relativePath.TrimStart('/', '\\').Replace("..", string.Empty);
        return Path.Combine(_options.DataDirectory, cleanPath);
    }

    public async Task SaveJsonAsync<T>(string relativePath, T data, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(data, _jsonOptions);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_options.EnableAtomicWrites)
            {
                var tempPath = $"{fullPath}.tmp.{Guid.NewGuid():N}";
                await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            else
            {
                await File.WriteAllTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T?> LoadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath))
        {
            return default;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return default;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_options.EnableAtomicWrites)
            {
                var tempPath = $"{fullPath}.tmp.{Guid.NewGuid():N}";
                await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            else
            {
                await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> LoadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            try
            {
                File.Delete(fullPath);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory = "", string searchPattern = "*.*", CancellationToken cancellationToken = default)
    {
        var targetDir = string.IsNullOrWhiteSpace(relativeDirectory)
            ? _options.DataDirectory
            : GetFullPath(relativeDirectory);

        if (!Directory.Exists(targetDir))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = Directory.GetFiles(targetDir, searchPattern, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_options.DataDirectory, f))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task<StorageHealthReport> GetStorageHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoriesCreated();

            if (!Directory.Exists(_options.DataDirectory))
            {
                return Task.FromResult(new StorageHealthReport(
                    IsHealthy: false,
                    RootDirectory: _options.DataDirectory,
                    TotalSizeBytes: 0,
                    TotalFilesCount: 0,
                    Files: Array.Empty<StorageFileEntry>(),
                    ErrorMessage: $"Data directory '{_options.DataDirectory}' does not exist and could not be created."
                ));
            }

            var dirInfo = new DirectoryInfo(_options.DataDirectory);
            var fileEntries = new List<StorageFileEntry>();
            long totalBytes = 0;

            foreach (var fi in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(_options.DataDirectory, fi.FullName);
                fileEntries.Add(new StorageFileEntry(relPath, fi.Length, fi.LastWriteTimeUtc));
                totalBytes += fi.Length;
            }

            return Task.FromResult(new StorageHealthReport(
                IsHealthy: true,
                RootDirectory: _options.DataDirectory,
                TotalSizeBytes: totalBytes,
                TotalFilesCount: fileEntries.Count,
                Files: fileEntries.OrderByDescending(f => f.LastModified).ToList()
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new StorageHealthReport(
                IsHealthy: false,
                RootDirectory: _options.DataDirectory,
                TotalSizeBytes: 0,
                TotalFilesCount: 0,
                Files: Array.Empty<StorageFileEntry>(),
                ErrorMessage: ex.Message
            ));
        }
    }
}
