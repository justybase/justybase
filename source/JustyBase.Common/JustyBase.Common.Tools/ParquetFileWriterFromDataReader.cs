using JustyBase.ImportExport.Export;
using System.Data;

namespace JustyBase.Common.Tools;

/// <summary>Host alias for <see cref="ParquetExportWriter"/>.</summary>
[Obsolete("Use JustyBase.ImportExport.Export.ParquetExportWriter")]
public sealed class ParquetFileWriterFromDataReader(IDataReader rdr, int groupSize = 32_768)
{
    private readonly ParquetExportWriter _inner = new(rdr, groupSize);

    public Task CreateFile(Stream fileStream) => _inner.CreateFile(fileStream);
}
