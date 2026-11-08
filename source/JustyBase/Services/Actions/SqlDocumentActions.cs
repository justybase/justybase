using System.Windows.Input;
using JustyBase.ViewModels;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services.Actions;

/// <summary>The document actions every JustyBase install has. They are declared once here and read by
/// the command palette, key bindings and context menus.</summary>
public static class SqlDocumentActions
{
    public const string Save = "document.save";
    public const string SaveAs = "document.saveAs";
    public const string FormatSql = "document.formatSql";
    public const string LocalHistory = "document.localHistory";
    public const string ShowInExplorer = "document.showInExplorer";
    public const string CopyFullPath = "document.copyFullPath";
    public const string RunSql = "document.runSql";
    public const string RunSqlSingle = "document.runSqlSingle";
    public const string StopSql = "document.stopSql";
    public const string CommentLines = "document.commentLines";
    public const string CopyWithFormats = "document.copyWithFormats";
    public const string Rename = "document.rename";
    public const string OpenFile = "document.openFile";

    public static void Register(ActionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new IdeAction(
            Save,
            "Save Document",
            "File",
            context => RunCommand(context, document => document.SaveFileCommand, "Save"),
            HasEditor,
            "Ctrl+S",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            SaveAs,
            "Save Document As…",
            "File",
            context => RunCommand(context, document => document.SaveFileCommand, "SaveAs"),
            HasEditor,
            "Ctrl+Shift+S",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            OpenFile,
            "Open SQL File in New Tab",
            "File",
            context => RunCommand(context, document => document.OpenFileCommand),
            HasDocument,
            null,
            OpenDocumentFirst));

        registry.Add(new IdeAction(
            FormatSql,
            "Format SQL",
            "SQL",
            context => RunCommand(context, document => document.FormatSqlCommand),
            HasEditor,
            "Ctrl+Alt+F",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            RunSql,
            "Run SQL",
            "SQL",
            context => RunCommand(context, document => document.RunSqlCommand, "Grid"),
            HasEditor,
            "F5",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            RunSqlSingle,
            "Run SQL as Single Command",
            "SQL",
            context => RunCommand(context, document => document.RunSqlCommand, "Grid|SingleBath"),
            HasEditor,
            "Ctrl+F5",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            StopSql,
            "Stop Running Queries",
            "SQL",
            context => RunCommand(context, document => document.AbortSqlCommand),
            context => context.Document?.SqlEditor is not null && context.Document.TasksToAbort > 0,
            null,
            context => "Nothing is running in this document."));

        registry.Add(new IdeAction(
            CommentLines,
            "Comment Selected Lines",
            "Edit",
            context => RunCommand(context, document => document.CommentLinesCommand),
            HasEditor,
            "Ctrl+Oem2",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            CopyWithFormats,
            "Copy with Formats",
            "Edit",
            context => RunCommand(context, document => document.CopyWithFormatsCommand),
            HasEditor,
            "Ctrl+Shift+C",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            Rename,
            "Rename Symbol",
            "Edit",
            context => RunCommand(context, document => document.RenameCommand),
            HasEditor,
            "F2",
            OpenEditorFirst));

        registry.Add(new IdeAction(
            LocalHistory,
            "Show Local History…",
            "File",
            context => RunCommand(context, document => document.ShowLocalHistoryCommand),
            HasDocument,
            "Ctrl+Alt+H",
            OpenDocumentFirst));

        registry.Add(new IdeAction(
            ShowInExplorer,
            "Show in Explorer",
            "Navigation",
            context => RunCommand(context, document => document.ShowInExplorerCommand),
            HasDocument,
            null,
            OpenDocumentFirst));

        registry.Add(new IdeAction(
            CopyFullPath,
            "Copy Full File Path",
            "Edit",
            context => RunCommand(context, document => document.CopyFullFilePathCommand),
            HasDocument,
            null,
            OpenDocumentFirst));
    }

    private static bool HasEditor(ActionContext context)
        => context.Document?.SqlEditor is not null;

    private static bool HasDocument(ActionContext context)
        => context.Document is not null;

    private static string OpenDocumentFirst(ActionContext context)
        => "Open a SQL document first.";

    private static string OpenEditorFirst(ActionContext context)
        => "Open a SQL document with an editor first.";

    private static Task RunCommand(
        ActionContext context,
        Func<SqlDocumentViewModel, ICommand?> command,
        object? parameter = null)
    {
        SqlDocumentViewModel? document = context.Document;
        ICommand? resolved = document is null ? null : command(document);
        if (resolved is null)
        {
            return Task.CompletedTask;
        }

        if (resolved.CanExecute(parameter))
        {
            resolved.Execute(parameter);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Workspace-level commands (tabs, panels, palettes) exposed through the same registry so the
/// keymap editor and the command palette can see the whole application.</summary>
public static class WorkspaceActions
{
    public const string NewDocument = "workspace.newDocument";
    public const string OpenFile = "workspace.openFile";
    public const string QuickOpen = "workspace.quickOpen";
    public const string GoToEverything = "workspace.goToEverything";
    public const string FindAction = "workspace.findAction";
    public const string FocusFileSearch = "workspace.focusFileSearch";
    public const string History = "workspace.history";
    public const string Settings = "workspace.settings";
    public const string Import = "workspace.import";
    public const string Keymap = "workspace.keymap";
    public const string CheckForUpdates = "workspace.checkForUpdates";
    public const string About = "workspace.about";

    public static void Register(ActionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        Add(registry, NewDocument, "New SQL Document", "File", "Ctrl+N",
            main => main.OpenNewTabCommand);
        Add(registry, OpenFile, "Open SQL File…", "File", "Ctrl+O",
            main => main.OpenFileCommand);
        Add(registry, QuickOpen, "Quick Open File…", "Navigation", "Ctrl+P",
            main => main.ShowQuickOpenCommand);
        Add(registry, GoToEverything, "Go to Everything…", "Navigation", "Ctrl+Shift+O",
            main => main.ShowGoToEverythingCommand);
        Add(registry, FindAction, "Find Action…", "Navigation", "Ctrl+Shift+P",
            main => main.ShowActionPaletteCommand);
        Add(registry, FocusFileSearch, "Focus File Search", "Navigation", "Ctrl+Shift+F",
            main => main.FocusFilesSearchCommand);
        Add(registry, History, "Query History", "View", "Ctrl+H",
            main => main.ShowHistoryCommand);
        Add(registry, Settings, "Settings", "View", "Ctrl+Alt+S",
            main => main.ShowSettingsCommand);
        Add(registry, Import, "Import Data…", "Data", "Ctrl+Shift+I",
            main => main.ImportCommand);
        Add(registry, Keymap, "Edit Keyboard Shortcuts…", "View", null,
            main => main.ShowKeymapEditorCommand);
        Add(registry, CheckForUpdates, "Check for Updates", "Help", null,
            main => main.CheckForUpdatesCommand);
        Add(registry, About, "About JustyBase", "Help", null,
            main => main.ShowAboutCommand);
    }

    private static void Add(
        ActionRegistry registry,
        string id,
        string title,
        string group,
        string? shortcut,
        Func<MainWindowViewModel, ICommand?> command)
    {
        registry.Add(new IdeAction(
            id,
            title,
            group,
            context => Run(context, command),
            context => context.Main is not null,
            shortcut,
            _ => "The main window is not available."));
    }

    private static Task Run(ActionContext context, Func<MainWindowViewModel, ICommand?> command)
    {
        MainWindowViewModel? main = context.Main;
        ICommand? resolved = main is null ? null : command(main);
        if (resolved?.CanExecute(context.Parameter) == true)
        {
            resolved.Execute(context.Parameter);
        }

        return Task.CompletedTask;
    }
}
