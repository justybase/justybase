using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Helpers.Interactions;
using System.Collections.ObjectModel;

namespace JustyBase.Models.Tools;

public partial class LogMessage : ObservableObject
{
    public DateTime Timestamp { get; set; }

    [ObservableProperty]
    public partial LogMessageType MessageType { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }
    public string Source { get; set; }
    [ObservableProperty]
    public partial string Message { get; set; }
    public ObservableCollection<StringPair> InnerMessages { get; set; } = [];

    public DataGridCollectionView InnerMessagesCollectionView { get; set; }
    private readonly IMessageForUserTools _messageForUserTools;
    public LogMessage(IMessageForUserTools messageForUserTools)
    {
        InnerMessagesCollectionView = new DataGridCollectionView(InnerMessages);
        _messageForUserTools = messageForUserTools;
    }

    public void AddInnerMessageInUiThread(string message, DateTime titleTime)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            InnerMessages.Insert(0, new StringPair() { PairTitle = titleTime, PairMessage = message });
        });
    }
}


