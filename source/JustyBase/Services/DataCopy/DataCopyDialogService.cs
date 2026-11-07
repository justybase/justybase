using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.ViewModels;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services.DataCopy;

public sealed class DataCopyDialogService : IDataCopyDialogService
{
    private readonly DataCopyService _dataCopyService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;

    public DataCopyDialogService(
        DataCopyService dataCopyService,
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers)
    {
        _dataCopyService = dataCopyService;
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
    }

    public async Task ShowAsync(
        string sourceConnectionName,
        string sourceDatabase,
        string sourceSchema,
        string sourceTable,
        bool isView)
    {
        var viewModel = new DataCopyViewModel(
            _dataCopyService,
            _generalApplicationData,
            _messageForUserTools,
            sourceConnectionName,
            sourceDatabase,
            sourceSchema,
            sourceTable,
            isView);

        var window = new DataCopyWindow(viewModel)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        await window.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
    }
}
