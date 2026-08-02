namespace JustyBase.Services;

public interface ILocalModelConfigurationService
{
    Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default);
    Task<List<string>> GetAvailableModelsAsync(string? backendId, CancellationToken ct = default);
}
