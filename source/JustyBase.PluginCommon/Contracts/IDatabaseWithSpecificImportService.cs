using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseWithSpecificImportService
{
    public DatabaseTypeEnum DatabaseType { get; init; }

    Task DbSpecificImportPart(IImportJob importJob, string randName, Action<string>? progress,
        bool tableExists = false);

    async Task<string> PerformImportFromXmlAsync(IXmlImportJob importJob, object data,
        Action<string>? messageAction)
    {
        var randName = StringExtension.RandomSuffix("IMP_");
        try
        {
            await importJob.AnalyzeXmlClipboardDataAndStoreLinesAsync(data);
            await DbSpecificImportPart(importJob, randName, messageAction);
        }
        catch (Exception ex)
        {
            randName = ex.Message;
        }

        return randName;
    }
}
