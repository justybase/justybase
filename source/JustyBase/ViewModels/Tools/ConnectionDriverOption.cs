namespace JustyBase.ViewModels.Tools;

public sealed record ConnectionDriverOption(
    string Id,
    string DisplayName,
    string Description,
    string DefaultPort,
    bool RequiresAuthentication,
    bool UsesFilePath);
