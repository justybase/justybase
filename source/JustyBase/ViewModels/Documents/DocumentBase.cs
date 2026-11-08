using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Services;

namespace JustyBase.ViewModels.Documents;


public partial class DocumentBaseVM : Document, IDeferredContentPresentation
{
    /// <summary>
    /// Document panes use the dockable VM as <see cref="DeferredContentControl"/> content;
    /// materialize immediately so float/split panes are not left blank while deferred.
    /// </summary>
    public virtual bool DeferContentPresentation => false;

    [ObservableProperty]
    public partial bool IsRecentlyFinished { get; set; } = false;

    public bool IsHistory => this is HistoryViewModel;
    public bool IsSettings => this is SettingsViewModel;
    public bool IsSql => this is SqlDocumentViewModel;
    public SqlDocumentViewModel? SqlDocument => this as SqlDocumentViewModel;
    public ICommand? TabShowInExplorerCommand => SqlDocument?.ShowInExplorerCommand;
    public ICommand? TabCopyFullFilePathCommand => SqlDocument?.CopyFullFilePathCommand;
    public ICommand? TabShowLocalHistoryCommand => SqlDocument?.ShowLocalHistoryCommand;
    public ICommand? TabRunActionCommand => SqlDocument?.RunActionCommand;
    public string? TabSaveShortcut => SqlDocument?.SaveShortcut;
    public string? TabSaveAsShortcut => SqlDocument?.SaveAsShortcut;
    public string? TabLocalHistoryShortcut => SqlDocument?.LocalHistoryShortcut;
    public string TabSaveToolTip => SqlDocument?.SaveShortcutText ?? "save";
    public string TabSaveAsToolTip => SqlDocument?.SaveAsShortcutText ?? "save as";
    public string TabLocalHistoryToolTip => SqlDocument?.LocalHistoryShortcutText ?? "local history";
    public bool IsImport => this is ImportViewModel;
    public bool IsEtl => this is EtlViewModel;
    private bool _skipCloseQuestion;
    private readonly bool _confirmDocumentClosing;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IDocumentCloseDecisionService _documentCloseDecisionService;
    public IActiveDocumentManager ActiveDocumentManager { get; }

    // Removed parameterless constructor with Service Locator

    protected DocumentBaseVM(
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDocumentCloseDecisionService documentCloseDecisionService,
        IActiveDocumentManager activeDocumentManager)
    {
        _confirmDocumentClosing = generalApplicationData.Config.ConfirmDocumentClosing;
        _messageForUserTools = messageForUserTools;
        _documentCloseDecisionService = documentCloseDecisionService;
        ActiveDocumentManager = activeDocumentManager;
        DockCapabilityHelper.SyncOverridesFromFlags(this);
        if (this is SqlDocumentViewModel sqlDocument)
        {
            sqlDocument.PropertyChanged += OnSqlDocumentPropertyChanged;
        }
    }

    private void OnSqlDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SqlDocumentViewModel.SaveShortcut):
                OnPropertyChanged(nameof(TabSaveShortcut));
                break;
            case nameof(SqlDocumentViewModel.SaveAsShortcut):
                OnPropertyChanged(nameof(TabSaveAsShortcut));
                break;
            case nameof(SqlDocumentViewModel.LocalHistoryShortcut):
                OnPropertyChanged(nameof(TabLocalHistoryShortcut));
                break;
            case nameof(SqlDocumentViewModel.SaveShortcutText):
                OnPropertyChanged(nameof(TabSaveToolTip));
                break;
            case nameof(SqlDocumentViewModel.SaveAsShortcutText):
                OnPropertyChanged(nameof(TabSaveAsToolTip));
                break;
            case nameof(SqlDocumentViewModel.LocalHistoryShortcutText):
                OnPropertyChanged(nameof(TabLocalHistoryToolTip));
                break;
        }
    }

    public override bool OnClose()
    {
        var shouldConfirmClose = _documentCloseDecisionService.ShouldConfirmClose(
            _confirmDocumentClosing,
            Title,
            _skipCloseQuestion,
            ActiveDocumentManager.IsLastDocument());

        if (shouldConfirmClose)
        {
            _ = ConfirmCloseAsync();
            return false;
        }
        return base.OnClose();
    }

    private async Task ConfirmCloseAsync()
    {
        var shouldClose = await _messageForUserTools
            .ShowConfirmationDialogAsync($"Do you really want to close the {Title} document ?", "Close ?");

        if (!shouldClose)
        {
            return;
        }

        _skipCloseQuestion = true;
        Factory.CloseDockable(this);
    }
}
