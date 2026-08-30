namespace CarnotCycleCircus.UI.Services;

public interface INativeFolderPicker
{
    Task<string?> PickDirectoryAsync(string? initialDirectory = null, string title = "Select Target Code Repository", CancellationToken cancellationToken = default);
}

public class DefaultNativeFolderPicker : INativeFolderPicker
{
    public Task<string?> PickDirectoryAsync(string? initialDirectory = null, string title = "Select Target Code Repository", CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(initialDirectory);
    }
}
