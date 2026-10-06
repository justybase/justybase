using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Ai.Models;
using JustyBase.Common.Models;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Ai.Services;
using JustyBase.Services;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools.Converters;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;

public sealed partial class AiChatViewModel
{
    private ChatMessage? _pendingConfirmationMessage;

    private async Task<bool> HandleToolConfirmationAsync(string toolName, string toolArgs)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            var confirmationMessage = new ChatMessage
            {
                Role = "tool-confirmation",
                Content = $"The model wants to use a tool. Allow execution?",
                Timestamp = DateTime.Now,
                IsToolConfirmation = true,
                ToolName = toolName,
                ToolArgs = toolArgs,
                ConfirmationPending = true
            };

            confirmationMessage.ConfirmationTcs = tcs;
            _pendingConfirmationMessage = confirmationMessage;

            Messages.Add(confirmationMessage);
            StatusMessage = $"Waiting for tool approval: {toolName}";
        });

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(5)));
        var result = completedTask == tcs.Task && tcs.Task.Result;
        
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            if (_pendingConfirmationMessage != null)
            {
                _pendingConfirmationMessage.ConfirmationPending = false;
                _pendingConfirmationMessage.Content = completedTask == tcs.Task
                    ? (result ? $"✓ Tool '{toolName}' approved" : $"✗ Tool '{toolName}' denied")
                    : $"✗ Tool '{toolName}' denied (approval timeout)";
            }
            _pendingConfirmationMessage = null;
            StatusMessage = completedTask == tcs.Task
                ? (result ? $"Tool approved: {toolName}" : $"Tool denied: {toolName}")
                : $"Tool approval timeout: {toolName}";
        });
        
        return result;
    }

    [RelayCommand]
    private void ConfirmTool(string allowValue)
    {
        if (!bool.TryParse(allowValue, out var allow))
        {
            StatusMessage = "Invalid tool confirmation response.";
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ConfirmTool] Called with allow={allow}, pendingMessage={_pendingConfirmationMessage != null}");
        
        if (_pendingConfirmationMessage?.ConfirmationTcs != null)
        {
            _pendingConfirmationMessage.ConfirmationPending = false;
            _pendingConfirmationMessage.Content = allow 
                ? $"✓ Tool '{_pendingConfirmationMessage.ToolName}' approved"
                : $"✗ Tool '{_pendingConfirmationMessage.ToolName}' denied";
            _pendingConfirmationMessage.ConfirmationTcs.TrySetResult(allow);
            _pendingConfirmationMessage = null;
        }
    }

    private string? GetCurrentSql()
    {
        if (Factory is IActiveDocumentManager docManager && docManager.ActiveSqlDocumentViewModel is { } docVm)
        {
            return docVm.GetCurrentTextFunc?.Invoke() ?? docVm.SqlEditor?.Text;
        }
        return null;
    }

    private (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? GetCurrentSqlEditorContext()
    {
        if (Factory is not IActiveDocumentManager docManager || docManager.ActiveSqlDocumentViewModel is not { } docVm || docVm.SqlEditor is null)
        {
            return null;
        }

        var editor = docVm.SqlEditor;
        var fullText = docVm.GetCurrentTextFunc?.Invoke() ?? editor.Text ?? string.Empty;
        var selectedText = editor.SelectedText ?? string.Empty;
        return (fullText, selectedText, editor.SelectionStart, editor.SelectionLength, editor.CaretOffset);
    }

    private bool UpdateCurrentSqlBuffer(string updatedSql)
    {
        if (Factory is not IActiveDocumentManager docManager || docManager.ActiveSqlDocumentViewModel is not { } docVm || docVm.SqlEditor is null)
        {
            return false;
        }

        var applyResult = false;
        void Apply()
        {
            docVm.SqlEditor.Text = updatedSql ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(docVm.Title) && !docVm.Title.EndsWith('*'))
            {
                docVm.Title += "*";
            }
            applyResult = true;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return applyResult;
        }

        Exception? applyError = null;
        using var done = new ManualResetEventSlim(false);
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try { Apply(); }
            catch (Exception ex) { applyError = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(5))) // ManualResetEventSlim
        {
            return false;
        }
        if (applyError is not null)
        {
            throw applyError;
        }

        return applyResult;
    }

    private (string ConnectionName, string DatabaseName)? GetActiveSqlContext()
    {
        if (Factory is IActiveDocumentManager docManager && docManager.ActiveSqlDocumentViewModel is { } docVm)
        {
            var connectionName = docVm.SelectedConnectionName;
            if (string.IsNullOrWhiteSpace(connectionName))
            {
                return null;
            }

            return (connectionName, docVm.SelectedDatabase ?? string.Empty);
        }

        return null;
    }
}
