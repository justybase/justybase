using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Helpers.Interactions;
using JustyBase.Models.Tools;
using JustyBase.Services;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
using JustyBase.ViewModels.Views;
using JustyBase.Views.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase;

public class ViewLocator : IDataTemplate, IRecyclingDataTemplate
{
    private readonly IServiceProvider _services;
    private static readonly Lock SyncFromRecycle = new();
    private static readonly Dictionary<object, SqlResultsView> SqlResultsViewCacheDictionary = [];

    public ViewLocator(IServiceProvider services)
    {
        _services = services;
    }

    public Control Build(object? data) => Build(data, null) ?? new TextBlock { Text = "Invalid Data Type" };

    public Control? Build(object? data, Control? existing)
    {
        if (data is null)
        {
            return null;
        }

        return BuildCore(data, existing);
    }

    private Control BuildCore(object dataViewModel, Control? existing)
    {
        switch (dataViewModel)
        {
            case SqlResultsViewModel when SqlResultsViewCacheDictionary.TryGetValue(dataViewModel, out var recycledInstance):
                return TryReturnRecycledControl(recycledInstance, existing)
                       ?? CreateSqlResultsView(dataViewModel);
            case SqlResultsViewModel:
                return CreateSqlResultsView(dataViewModel);
            // SQL documents are owned by SqlDocumentDataTemplate + Dock content cache.
            case SqlDocumentViewModel:
                return new TextBlock { Text = "SqlDocumentViewModel requires SqlDocumentDataTemplate" };
            case AiChatViewModel:
                return new AiChatView();
            case DbSchemaViewModel:
                {
                    var avaloniaHelpers = _services.GetRequiredService<IAvaloniaSpecificHelpers>();
                    var addNewConnectionVm = _services.GetRequiredService<AddNewConnectionViewModel>();
                    return new DbSchemaView(avaloniaHelpers, addNewConnectionVm);
                }
            case SettingsViewModel:
                {
                    var avaloniaHelpers = _services.GetRequiredService<IAvaloniaSpecificHelpers>();
                    var fontService = _services.GetRequiredService<IDocumentFontService>();
                    return new Views.Documents.SettingsView(avaloniaHelpers, fontService);
                }
            case DbSchemaModel:
                return new TextBox
                {
                    [!TextBox.TextProperty] = CompiledBindingFactory.OneWay<DbSchemaModel, string>(
                        nameof(DbSchemaModel.Name),
                        node => node.Name),
                    VerticalAlignment = VerticalAlignment.Center
                };
            case AboutViewModel aboutViewModel:
                return new Views.About(aboutViewModel);
            case MainViewModel:
                return new Views.MainView();
            case MainWindowViewModel:
                return new Views.MainWindow(
                    _services.GetRequiredService<INotificationManagerProvider>(),
                    _services.GetRequiredService<IMessageForUserTools>(),
                    _services.GetRequiredService<AboutViewModel>());
            case NetezzaMaintenanceDialogViewModel viewModel:
                return new Views.OtherDialogs.NetezzaMaintenanceDialog(viewModel);
            case NetezzaDistributionChartViewModel viewModel:
                return new Views.OtherDialogs.NetezzaDistributionChartWindow(viewModel);
            case SqlParameterViewModel:
                return new Views.SqlParameterWindow();
            case GitDiffViewModel:
                return new Views.OtherDialogs.GitDiffWindow();
            case FileDiffViewModel:
                return new Views.OtherDialogs.FileDiffWindow();
            case QuickOpenViewModel viewModel:
                return new Views.OtherDialogs.QuickOpenWindow(viewModel);
            case AskForConfirmViewModel:
                return new Views.OtherDialogs.AskForConfirm();
            case SnippetControlViewModel viewModel:
                return new SnippetControl(viewModel);
            case DbObjectQuickMenuViewModel:
                return new Views.ToolTipViews.DbObjectQuickMenu();
            case GitDiffDocumentViewModel:
                return new Views.Documents.GitDiffDocumentView();
            case HistoryViewModel:
                return new Views.Documents.HistoryView();
            case EtlViewModel:
                return new Views.Documents.EtlView();
            case ImportViewModel:
                return new Views.Documents.ImportView();
            case GitViewModel:
                return new GitView();
            case FileExplorerViewModel:
                return new FileExplorerView();
            case FileSearchViewModel:
                return new FileSearchView();
            case LogToolViewModel:
                return new LogToolView();
            case NetezzaSessionMonitorViewModel:
                return new NetezzaSessionMonitorView();
            case SchemaSearchViewModel:
                return new SchemaSearchView();
            case SqlDiagnosticsViewModel:
                return new SqlDiagnosticsView();
            case SqlOutlineViewModel:
                return new SqlOutlineView();
            case SqlResultsFastViewModel:
                return new SqlResultsFastView();
            case VariablesViewModel:
                return new VariablesView();
        }

        return new TextBlock { Text = "No view registered for " + dataViewModel.GetType().FullName };
    }

    private static Control? TryReturnRecycledControl(Control recycledInstance, Control? existing)
    {
        if (ReferenceEquals(recycledInstance, existing))
        {
            return recycledInstance;
        }

        return TryDetachFromParent(recycledInstance) ? recycledInstance : null;
    }

    private SqlResultsView CreateSqlResultsView(object dataViewModel)
    {
        var services = _services.GetRequiredService<ISqlResultsViewServices>();
        var newInstance = new SqlResultsView(services);
        lock (SyncFromRecycle)
        {
            SqlResultsViewCacheDictionary[dataViewModel] = newInstance;
        }
        return newInstance;
    }

    private static bool TryDetachFromParent(Control control)
    {
        while (true)
        {
            var parent = control.Parent ?? control.GetVisualParent() as Control;
            if (parent is null)
            {
                return true;
            }

            var detached = parent switch
            {
                Panel panel => panel.Children.Remove(control),
                Decorator decorator when ReferenceEquals(decorator.Child, control) => DetachDecoratorChild(decorator),
                ContentControl contentControl when ReferenceEquals(contentControl.Content, control)
                    => DetachContentControl(contentControl),
                ContentPresenter presenter => TryDetachFromContentPresenter(presenter, control),
                _ => false
            };

            if (!detached)
            {
                return false;
            }

            if (control.Parent is null && control.GetVisualParent() is null)
            {
                return true;
            }
        }
    }

    private static bool DetachDecoratorChild(Decorator decorator)
    {
        decorator.Child = null;
        return true;
    }

    private static bool DetachContentControl(ContentControl contentControl)
    {
        contentControl.SetCurrentValue(ContentControl.ContentProperty, null);
        return true;
    }

    private static bool TryDetachFromContentPresenter(ContentPresenter presenter, Control control)
    {
        if (!ReferenceEquals(presenter.Child, control) && !ReferenceEquals(presenter.Content, control))
        {
            return false;
        }

        presenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
        presenter.UpdateChild();
        return control.GetVisualParent() is null;
    }

    public static void RemoveFromCache(IDockable dock)
    {
        lock (SyncFromRecycle)
        {
            SqlResultsViewCacheDictionary.Remove(dock);
        }
    }

    public bool Match(object? data)
    {
        return data is ObservableObject or IDockable;
    }
}
