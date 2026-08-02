using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Documents;
using JustyBase.Views.ToolTipViews;

namespace JustyBase.Views.Documents;

public partial class SqlDocumentView : UserControl
{
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;

    public SqlDocumentView(IMessageForUserTools messageForUserTools, ISimpleLogger simpleLogger)
    {
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
        InitializeComponent();
        SqlEditor.TextArea.RightClickMovesCaret = true;
        SqlEditor.DataContextChanged += TextEditor_DataContextChanged;
        SqlEditor.KeyDown += TextEditor_KeyDownAsync;
        SetupDnd();
    }

    private readonly Flyout _quickMenuFlyout = new()
    {
        Content = new DbObjectQuickMenu(),
        ShowMode = FlyoutShowMode.Standard
    };

    private async void TextEditor_KeyDownAsync(object? sender, KeyEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        if (e.Key == Key.F4)
        {
            if (_quickMenuFlyout.Content is not DbObjectQuickMenu quickMenu)
            {
                return;
            }

            quickMenu.DataContext = new DbObjectQuickMenuViewModel()
            {
                ObjectTitle = this.SqlEditor.GetTappedWord(),
                SqlDocVM = viewModel,
                CloseAction = _quickMenuFlyout.Hide
            };
            _quickMenuFlyout.ShowAt(this, true);
        }
        else if (e.Key is Key.RightCtrl or Key.F6)
        {
            await viewModel.JumpToSelectedItem();
        }
        else if (e.Key == Key.F7)
        {
            await viewModel.SelectSelectedItem();
        }
    }

    private SqlDocumentViewModel? ViewModel => this.DataContext as SqlDocumentViewModel;

    private readonly DataFormat _fileContentsDataFormat = DataFormat.CreateBytesPlatformFormat("FileContents");
    private void SetupDnd()
    {
        void DragOver(object sender, DragEventArgs e)
        {
            //files = special care
            if (e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(_fileContentsDataFormat))
            {
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }
            return;
        }

        async void Drop(object sender, DragEventArgs e)
        {
            var viewModel = ViewModel;
            if (viewModel is null)
            {
                return;
            }

            if (e.DataTransfer.Contains(DataFormat.File))
            {
                //SqlEditor.AppendText(string.Join(Environment.NewLine, e.Data.GetFileNames()));
                var filenameX = e.DataTransfer.TryGetFiles();
                if (filenameX is not null)
                {
                    List<string> filenamesToOpen = filenameX.Select(o => o.Path.LocalPath)
                        .Where(o => IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(Path.GetExtension(o)))
                        .ToList();
                    viewModel.ActiveDocumentManager.AddNewDocumentFromFile(filenamesToOpen);

                    foreach (var item in filenameX.Select(o => o.Path.LocalPath).Where(p => !IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(Path.GetExtension(p))))
                    {
                        int index = viewModel.SelectedConnectionIndex;
                        var doc = viewModel.ActiveDocumentManager.AddNewDocument("IMPORT IN PROGRESS");
                        doc.SelectedConnectionIndex = index;
                        await doc.ImportFromFilePath(item);
                    }
                }
            }
            else if (e.DataTransfer.Contains(_fileContentsDataFormat))
            {
                try
                {
                    var fileContents = e.DataTransfer.GetItems(_fileContentsDataFormat);
                    //var t2 = e.Data.Get("FileGroupDescriptor");
                    //var t3 = e.Data.Get("FileGroupDescriptorW");
                    //var t4 = e.Data.Get("Text");
                    //var formats = e.Data.GetDataFormats();
                    if (fileContents is MemoryStream memoryStream)
                    {
                        using var streamreader = new StreamReader(memoryStream);
                        string droppedSql = await streamreader.ReadToEndAsync();
                        viewModel.ActiveDocumentManager.AddNewDocument(droppedSql);
                    }
                }
                catch (IOException ex)
                {
                    _simpleLogger.TrackError(ex, isCrash: false);
                    _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
                }
            }
        }
        AddHandler(DragDrop.DropEvent, Drop);
        AddHandler(DragDrop.DragOverEvent, DragOver);
    }

    private bool _initialized;
    private SqlDocumentViewModel? _boundViewModel;

    private void TextEditor_DataContextChanged(object sender, EventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        // New VM on this view (should be rare with 1:1 cache) — reset one-shot UI wiring.
        var vmChanged = !ReferenceEquals(_boundViewModel, viewModel);
        if (vmChanged)
        {
            _initialized = false;
            _boundViewModel = viewModel;
        }

        // Always bind this view's editor. Tab reorder recreates SqlDocumentView (Dock Remove+Insert);
        // the VM must follow the visible editor so OnSqlEditorChanged can transfer live text.
        if (!ReferenceEquals(viewModel.SqlEditor, SqlEditor))
        {
            viewModel.SqlEditor = SqlEditor;
        }

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var currentOptions = new MenuItem() { Header = "Current options", IsEnabled = true };
        foreach (var item in viewModel.CurrentOptionsList)
        {
            currentOptions.Items.Add(new MenuItem() { Header = item.OptionHeader, Command = item.OptionCommand, CommandParameter = item.OptionHeader });
        }
        rightMenu.Items.Insert(0, currentOptions);
    }
}
