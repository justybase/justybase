using JustyBase.PluginCommon.Models;

namespace JustyBase.PluginCommon.Contracts;

public interface IDatabaseService :
    IDatabaseWithSpecificImportService,
    IDatabaseConnectionInfo,
    IDatabaseSchemaQueryService,
    IDatabaseDdlTextService
{
    /// <summary>Engine feature switches used by the host to gate schema/editor actions.</summary>
    DatabaseCapabilities Capabilities { get; }
}
