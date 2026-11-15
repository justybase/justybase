using System.Data;
using System.Diagnostics.CodeAnalysis;

namespace JustyBase.Helpers;

/// <summary>
/// Maps a result <see cref="TypeCode"/> to the CLR type a <c>DbDataReader</c>
/// reports. The switch is explicit so trimming can verify the contract for
/// every arm.
/// </summary>
internal static class ResultFieldTypeMapper
{
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public static Type FromTypeCode(TypeCode typeCode) => typeCode switch
    {
        TypeCode.Boolean => typeof(bool),
        TypeCode.Byte => typeof(byte),
        TypeCode.Char => typeof(char),
        TypeCode.DateTime => typeof(DateTime),
        TypeCode.DBNull => typeof(DBNull),
        TypeCode.Decimal => typeof(decimal),
        TypeCode.Double => typeof(double),
        TypeCode.Int16 => typeof(short),
        TypeCode.Int32 => typeof(int),
        TypeCode.Int64 => typeof(long),
        TypeCode.SByte => typeof(sbyte),
        TypeCode.Single => typeof(float),
        TypeCode.String => typeof(string),
        TypeCode.UInt16 => typeof(ushort),
        TypeCode.UInt32 => typeof(uint),
        TypeCode.UInt64 => typeof(ulong),
        // TypeCode.Empty/Object (and anything unknown) carry heterogeneous
        // values; object keeps GetFieldType truthful without reflection.
        _ => typeof(object),
    };
}
