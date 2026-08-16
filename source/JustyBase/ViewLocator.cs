using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;
using JustyBase.Helpers;
using JustyBase.Models.Tools;
using JustyBase.Services;
using JustyBase.ViewModels.Documents;
using JustyBase.ViewModels.Tools;
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
        }

        var name = dataViewModel.GetType().FullName?.Replace("ViewModel", "View");
        if (name is null)
        {
            return new TextBlock { Text = "Invalid Data Type" };
        }

        var type = Type.GetType(name);
        if (type is null) return new TextBlock { Text = "Not Found: " + name };
        object? instance = Activator.CreateInstance(type);
        if (instance is DbSchemaModel)
        {
            return new TextBox
            {
                [!TextBox.TextProperty] = CompiledBindingFactory.OneWay<DbSchemaModel, string>(
                    nameof(DbSchemaModel.Name),
                    node => node.Name),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (instance is not null)
        {
            return (Control)instance;
        }
        return new TextBlock { Text = "Create Instance Failed: " + type.FullName };
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
