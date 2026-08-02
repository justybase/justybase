using JustyBase.Common.Contracts;
using JustyBase.Services;

namespace JustyBase.ViewModels.Documents;
public class EtlViewModel : DocumentBaseVM
{
    public string EtlMsg { get; set; }

    public EtlViewModel(IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools, IDocumentCloseDecisionService documentCloseDecisionService, IActiveDocumentManager activeDocumentManager)
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        Title = "ETL — not available";
        EtlMsg = "ETL designer is not available yet.";
    }
}

