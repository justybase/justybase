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
    [RelayCommand]
    private void SwitchMode(string modeSlug)
    {
        var mode = ChatModeExtensions.FromSlug(modeSlug);
        CurrentMode = mode;
        CurrentModeDisplayName = mode.ToDisplayName();
        StatusMessage = $"Switched to {CurrentModeDisplayName} mode";
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        if (value >= 0 && value < AvailableModes.Count)
        {
            var mode = AvailableModes[value].Mode;
            if (mode != CurrentMode)
            {
                CurrentMode = mode;
                CurrentModeDisplayName = mode.ToDisplayName();
                StatusMessage = $"Switched to {CurrentModeDisplayName} mode";
            }
        }
    }

    [RelayCommand]
    private void ToggleTodoPanel()
    {
        ShowTodoPanel = !ShowTodoPanel;
    }

    [RelayCommand]
    private void ToggleSlashCommandMenu()
    {
        ShowSlashCommandMenu = !ShowSlashCommandMenu;
    }

    partial void OnInputTextChanged(string value)
    {
        if (SlashCommand.IsSlashCommand(value))
        {
            var filter = value.TrimStart('/');
            SlashCommandFilter = filter;
            ShowSlashCommandMenu = true;
            ShowMentionMenu = false;
            UpdateFilteredSlashCommands(filter);
        }
        else if (ContainsMentionTrigger(value))
        {
            var mentionFilter = ExtractMentionFilter(value);
            ShowSlashCommandMenu = false;
            ShowMentionMenu = true;
            _ = SearchMentionsAsync(mentionFilter);
        }
        else
        {
            ShowSlashCommandMenu = false;
            ShowMentionMenu = false;
        }
    }

    private static bool ContainsMentionTrigger(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var atIndex = text.LastIndexOf('@');
        if (atIndex < 0)
            return false;

        var afterAt = text[(atIndex + 1)..];
        if (afterAt.Contains(' ', StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string ExtractMentionFilter(string text)
    {
        var atIndex = text.LastIndexOf('@');
        if (atIndex < 0)
            return string.Empty;

        return text[(atIndex + 1)..];
    }

    private async Task SearchMentionsAsync(string filter)
    {
        try
        {
            var suggestions = new List<MentionItem>();

            suggestions.Add(new MentionItem { Name = "current-sql", Type = MentionType.SqlEditor, Description = "Current SQL editor content" });
            suggestions.Add(new MentionItem { Name = "results", Type = MentionType.Results, Description = "Current query results" });

            var context = GetActiveSqlContext();
            if (context.HasValue)
            {
                var (connectionName, databaseName) = context.Value;

                var connectionItem = new MentionItem
                {
                    Name = connectionName,
                    Type = MentionType.Connection,
                    Description = $"Active connection"
                };
                suggestions.Add(connectionItem);

                if (!string.IsNullOrWhiteSpace(databaseName))
                {
                    var dbItem = new MentionItem
                    {
                        Name = databaseName,
                        Type = MentionType.Database,
                        Description = $"Database in {connectionName}"
                    };
                    suggestions.Add(dbItem);
                }

                await Task.Run(() =>
                {
                    var dbService = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
                    if (dbService is null)
                    {
                        return;
                    }

                    var db = string.IsNullOrWhiteSpace(databaseName) ? dbService.Database : databaseName;

                    try
                    {
                        var schemas = dbService.GetSchemas(db, "").Take(20);
                                foreach (var schema in schemas)
                                {
                                    if (!string.IsNullOrWhiteSpace(filter) &&
                                        !schema.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    suggestions.Add(new MentionItem
                                    {
                                        Name = schema,
                                        Type = MentionType.Schema,
                                        Database = db,
                                        Description = $"Schema in {db}"
                                    });
                                }

                                foreach (var schema in dbService.GetSchemas(db, "").Take(5))
                                {
                                    var tables = dbService.GetDbObjects(db, schema, filter, TypeInDatabaseEnum.Table).Take(10);
                                    foreach (var table in tables)
                                    {
                                        suggestions.Add(new MentionItem
                                        {
                                            Name = table.Name,
                                            FullName = $"{db}.{schema}.{table.Name}",
                                            Type = MentionType.Table,
                                            Schema = schema,
                                            Database = db,
                                            Description = table.Desc
                                        });
                                    }

                                    var views = dbService.GetDbObjects(db, schema, filter, TypeInDatabaseEnum.View).Take(5);
                                    foreach (var view in views)
                                    {
                                        suggestions.Add(new MentionItem
                                        {
                                            Name = view.Name,
                                            FullName = $"{db}.{schema}.{view.Name}",
                                            Type = MentionType.View,
                                            Schema = schema,
                                            Database = db,
                                            Description = view.Desc
                                        });
                                    }

                                    var procs = dbService.GetDbObjects(db, schema, filter, TypeInDatabaseEnum.Procedure).Take(5);
                                    foreach (var proc in procs)
                                    {
                                        suggestions.Add(new MentionItem
                                        {
                                            Name = proc.Name,
                                            FullName = $"{db}.{schema}.{proc.Name}",
                                            Type = MentionType.Procedure,
                                            Schema = schema,
                                            Database = db,
                                            Description = proc.Desc
                                        });
                                    }
                                }
                    }
                    catch (Exception ex)
                    {
                        _logger.TrackError(ex, isCrash: false);
                    }
                });
            }

            var filtered = string.IsNullOrWhiteSpace(filter)
                ? suggestions
                : suggestions.Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                        (m.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MentionSuggestions.Clear();
                foreach (var item in filtered.Take(20))
                {
                    MentionSuggestions.Add(item);
                }
                ShowMentionMenu = MentionSuggestions.Count > 0;
            });
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
        }
    }

    [RelayCommand]
    public void InsertMentionItem(MentionItem item)
    {
        var atIndex = InputText.LastIndexOf('@');
        if (atIndex >= 0)
        {
            var beforeAt = InputText[..atIndex];
            var afterMention = string.Empty;
            
            var spaceAfter = InputText.IndexOf(' ', atIndex);
            if (spaceAfter > atIndex)
            {
                afterMention = InputText[spaceAfter..];
            }

            InputText = $"{beforeAt}@{item.InsertText} {afterMention}".TrimEnd();
        }

        ShowMentionMenu = false;
    }

    private void UpdateFilteredSlashCommands(string filter)
    {
        AvailableSlashCommands.Clear();
        var commands = string.IsNullOrWhiteSpace(filter)
            ? SlashCommand.BuiltInCommands
            : SlashCommand.GetMatchingCommands("/" + filter);
        
        foreach (var cmd in commands)
        {
            AvailableSlashCommands.Add(cmd);
        }
    }

    [RelayCommand]
    public void ExecuteSlashCommand(SlashCommand command)
    {
        if (command.TargetMode.HasValue)
        {
            SwitchMode(command.TargetMode.Value.ToSlug());
        }

        if (command.Action == "clear")
        {
            NewChat();
        }

        if (!string.IsNullOrWhiteSpace(command.AutoContext))
        {
            InputText = command.AutoContext switch
            {
                "schema" => "Search schema objects: ",
                "history" => "Search SQL history for: ",
                "analyze" => "Analyze current SQL for Netezza optimization",
                "explain" => "Explain current SQL query",
                _ => string.Empty
            };
        }

        ShowSlashCommandMenu = false;
    }

    public void InsertMention(string mention)
    {
        var cursorPos = InputText.Length;
        var beforeCursor = InputText;
        var afterCursor = string.Empty;
        
        var lastAtIndex = beforeCursor.LastIndexOf('@');
        if (lastAtIndex >= 0 && cursorPos >= lastAtIndex)
        {
            beforeCursor = InputText[..lastAtIndex];
        }

        InputText = $"{beforeCursor}@{mention} {afterCursor}".TrimStart();
    }

    public void UpdateTodoListFromJson(string todosJson)
    {
        CurrentTodoList.UpdateFromJson(todosJson);
        HasTodoItems = CurrentTodoList.TotalCount > 0;
    }

    partial void OnCurrentModeChanged(ChatMode value)
    {
        CurrentModeDisplayName = value.ToDisplayName();
        _chatService.SetMode(value);
        for (var i = 0; i < AvailableModes.Count; i++)
        {
            if (AvailableModes[i].Mode == value)
            {
                SelectedModeIndex = i;
                break;
            }
        }
    }

    partial void OnShowCodexEmailChanged(bool value)
    {
        RefreshCodexAccountState();
    }

    [RelayCommand]
    private void ToggleShowCodexEmail()
    {
        ShowCodexEmail = !ShowCodexEmail;
    }
}
