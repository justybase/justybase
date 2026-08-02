using Avalonia.Controls.Templates;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.ViewModels.Documents;
using JustyBase.Views.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase;

/// <summary>
/// Creates a SQL document view for its matching view model.
/// Dock owns the resulting control afterwards; with CacheDocumentTabContent enabled,
/// the control stays alive for the lifetime of its document tab.
/// </summary>
public sealed class SqlDocumentDataTemplate(IServiceProvider services) : IDataTemplate
{
    private readonly IServiceProvider _services = services;

    public Control? Build(object? data)
    {
        if (data is not SqlDocumentViewModel)
        {
            return null;
        }

        return new SqlDocumentView(
            _services.GetRequiredService<IMessageForUserTools>(),
            _services.GetRequiredService<ISimpleLogger>());
    }

    public bool Match(object? data) => data is SqlDocumentViewModel;
}
