using JustyBase.PluginCommon.Enums;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseService :
    IDatabaseWithSpecificImportService,
    IDatabaseConnectionInfo,
    IDatabaseSchemaQueryService,
    IDatabaseDdlTextService
{
    public const DatabaseTypeEnum WHO_I_AM_CONST = DatabaseTypeEnum.NotSupportedDatabase;
}
