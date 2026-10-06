namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseService :
    IDatabaseWithSpecificImportService,
    IDatabaseConnectionInfo,
    IDatabaseSchemaQueryService,
    IDatabaseDdlTextService
{
}
