using JustyBase.Common.Contracts;
using JustyBase.ViewModels;

namespace JustyBase.Services.Updates;

/// <summary>
/// Persists workspace state right before a Velopack update restart.
/// <c>ApplyUpdatesAndRestart</c> terminates the process without raising window-closing
/// handlers, so every restart path must save explicitly through this seam.
/// </summary>
public interface IApplicationRestartPreparation
{
    void SaveBeforeRestart();
}

public sealed class ApplicationRestartPreparation(
    DockFactory dockFactory,
    IGeneralApplicationData generalApplicationData) : IApplicationRestartPreparation
{
    public void SaveBeforeRestart()
    {
        dockFactory.SaveStartupSqlAndFiles();
        generalApplicationData.SaveConfig();
    }
}
