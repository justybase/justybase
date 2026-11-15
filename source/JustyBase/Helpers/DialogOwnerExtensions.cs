using Avalonia.Controls;

namespace JustyBase;

/// <summary>
/// Shows modal dialogs when a main window exists and falls back to a non-modal
/// window otherwise (designer/headless paths, startup errors).
/// </summary>
internal static class DialogOwnerExtensions
{
    /// <summary>Shows the dialog modally for <paramref name="owner"/>, or non-modally without one.</summary>
    public static Task ShowDialogOrShow(this Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.Show();
            return Task.CompletedTask;
        }

        return dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Shows the dialog and returns its result; without an owner the dialog is
    /// shown non-modally and <c>default</c> is returned immediately.
    /// </summary>
    public static Task<TResult> ShowDialogOrShow<TResult>(this Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.Show();
            return Task.FromResult(default(TResult)!);
        }

        return dialog.ShowDialog<TResult>(owner);
    }
}
