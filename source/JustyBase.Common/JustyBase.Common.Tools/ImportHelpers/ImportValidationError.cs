using JustyBase.PluginCommon.Models;

namespace JustyBase.Common.Tools.ImportHelpers;

public sealed record ImportValidationError(
    string SheetName,
    int RowNumber,
    int ColumnIndex,
    string ColumnName,
    DbTypeWithSize SelectedType,
    string Value,
    string Message)
{
    public override string ToString()
        => $"Sheet '{SheetName}', row {RowNumber}, column '{ColumnName}', type {SelectedType}: {Message} (value: '{Value}')";
}

public sealed class ImportValidationException : Exception
{
    public ImportValidationException(IReadOnlyList<ImportValidationError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(e => e.ToString())))
    {
        Errors = errors;
    }

    public IReadOnlyList<ImportValidationError> Errors { get; }
}
