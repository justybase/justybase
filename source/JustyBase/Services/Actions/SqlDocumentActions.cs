using System.Windows.Input;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services.Actions;

/// <summary>The document actions every JustyBase install has. They are declared once here and read by
/// the command palette; menus keep their existing bindings until each is migrated.</summary>
public static class SqlDocumentActions
{
    public const string Save = "document.save";
    public const string SaveAs = "document.saveAs";
    public const string FormatSql = "document.formatSql";
    public const string LocalHistory = "document.localHistory";
    public const string ShowInExplorer = "document.showInExplorer";
    public const string CopyFullPath = "document.copyFullPath";

    public static void Register(ActionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new IdeAction(
            Save,
            "Save Document",
            "File",
            context => RunCommand(context, document => document.SaveFileCommand, "Save"),
            HasEditor,
            "Ctrl+S"));

        registry.Add(new IdeAction(
            SaveAs,
            "Save Document As…",
            "File",
            context => RunCommand(context, document => document.SaveFileCommand, "SaveAs"),
            HasEditor,
            "Ctrl+Shift+S"));

        registry.Add(new IdeAction(
            FormatSql,
            "Format SQL",
            "SQL",
            context => RunCommand(context, document => document.FormatSqlCommand),
            HasEditor));

        registry.Add(new IdeAction(
            LocalHistory,
            "Show Local History…",
            "File",
            context => RunCommand(context, document => document.ShowLocalHistoryCommand),
            HasDocument));

        registry.Add(new IdeAction(
            ShowInExplorer,
            "Show in Explorer",
            "Navigation",
            context => RunCommand(context, document => document.ShowInExplorerCommand),
            HasDocument));

        registry.Add(new IdeAction(
            CopyFullPath,
            "Copy Full File Path",
            "Edit",
            context => RunCommand(context, document => document.CopyFullFilePathCommand),
            HasDocument));
    }

    private static bool HasEditor(ActionContext context)
        => context.Document?.SqlEditor is not null;

    private static bool HasDocument(ActionContext context)
        => context.Document is not null;

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
