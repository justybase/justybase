using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using System.Collections.ObjectModel;
using System.Text;
namespace JustyBase.ViewModels.Tools;

public sealed partial class LogToolViewModel : Tool
{
    public DataGridCollectionView LogCollectionView { get; set; }

    private readonly ObservableCollection<LogMessage> _logItems = [];

    [ObservableProperty]
    public partial LogMessage SelectedLogItem { get; set; }

    private readonly IClipboardService _clipboardService;
    private readonly IMessageForUserTools _messageForUserTools;
    public LogToolViewModel(IFactory factory, IClipboardService clipboardService, IMessageForUserTools messageForUserTools)
    {
        this.Factory = factory;
        _clipboardService = clipboardService;
        _messageForUserTools = messageForUserTools;
        LogCollectionView = new DataGridCollectionView(_logItems)
        {
            Filter = FilterRecords
        };
        LogCollectionView.SortDescriptions.Add(DataGridSortDescription.FromPath("Timestamp", System.ComponentModel.ListSortDirection.Descending));
    }
    private bool FilterRecords(object o)
    {
        var item = o as LogMessage;
        if (string.IsNullOrEmpty(CurrentId))
        {
            return true;
        }
        if (item is not null && item.Source == CurrentId)
        {
            return true;
        }
        return false;
    }
    public string CurrentId { get; set; } = "";


    private void AddNewLog(LogMessage message)
    {
        _logItems.Add(message);
    }

    public void AddLog(string message, LogMessageType MessageType, string title, DateTime dateTime, string source)
    {
        AddLog(new LogMessage(_messageForUserTools)
        {
            Message = message,
            MessageType = MessageType,
            Title = title,
            Timestamp = dateTime,
            Source = source
        });
    }

    public void AddLog(LogMessage logMessage)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            AddNewLog(logMessage);
            SelectedLogItem = logMessage;
        });
    }

    public IReadOnlyList<LogMessage> GetLogSnapshot()
    {
        return _logItems.ToList();
    }

    public void SwitchLogs(string id)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            CurrentId = id;
            LogCollectionView.Refresh();//triger filtering
        });
    }


    [RelayCommand]
    private void ClearLog()
    {
        (LogCollectionView.SourceCollection as ObservableCollection<LogMessage>)?.Clear();
        LogCollectionView.Refresh();
    }
    [RelayCommand]
    private async Task Copy()
    {
        if (SelectedLogItem is not null)
        {
            StringBuilder sb = new();
            sb.AppendLine("################################");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Title: {SelectedLogItem.Title}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Message: {SelectedLogItem.Message}");
            sb.AppendLine("##Inner messages##");
            foreach (var item in SelectedLogItem.InnerMessages)
            {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"   title:{item.PairTitle}");
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"   message:{item.PairMessage}");
                sb.AppendLine();
            }
            sb.AppendLine("################################");
            await _clipboardService.SetTextAsync(sb.ToString());
        }
    }
}





