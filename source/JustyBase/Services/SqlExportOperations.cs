using System.Text;
using JustyBase.Common.Tools.ImportHelpers.XML;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;

namespace JustyBase.Services;

public class SqlExportOperations : ISqlExportOperations
{
    public string BuildPasteAsIn(string pasteType, string clipboardText)
    {
        return StringExtension.PasteAsInHelper(pasteType, clipboardText);
    }

    public string BuildSelectUnionFromClipboard(string clipboardText)
    {
        clipboardText = clipboardText.TrimEnd('\r', '\n');
        clipboardText = clipboardText.Replace("\r", "");

        var lines = StringExtension.ClipboardTextToLinesArray(clipboardText);
        if (lines is null || lines.Count == 0)
        {
            return string.Empty;
        }
        var firstRange = lines.FirstOrDefault();
        var headers = clipboardText[firstRange].Split('\t').Select(arg => arg.Trim()).ToArray();

        var allLetters = !headers.Where(x => x.Length == 0 || char.IsAsciiLetter(x[0]) == false).Any();

        StringBuilder sb = new();
        sb.AppendLine("--REGION clipboard data");

        int i = 1;
        foreach (var actualRange in lines)
        {
            if (allLetters && i == 1)
            {
                i++;
                continue;
            }
            var v1 = clipboardText[actualRange].AsSpan().MySplit2('\t');

            if (actualRange.Start.Equals(actualRange.End))
            {
                continue;
            }

            if (i == 1)
            {
                sb.Append("SELECT");
            }
            else
            {
                sb.Append("UNION ALL SELECT");
            }
            for (int j = 0; j < v1.Count; j++)
            {
                var val = DbXMLImportJob.GetValueStringRepresentationWithType(out DbSimpleType nz, v1[j]);
                if (nz == DbSimpleType.Integer && v1[j].Trim().Length == 11 && headers[j].Contains("PESEL", StringComparison.OrdinalIgnoreCase))
                {
                    nz = DbSimpleType.Nvarchar;
                    val = $"'{v1[j].Trim()}'";
                }
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" {(val == "" ? "null" : val)} AS {headers[j].NormalizeDbColumnName().Trim()}");
                if (j != v1.Count - 1)
                {
                    sb.Append(',');
                }
            }
            sb.AppendLine();
            i++;
        }

        sb.AppendLine("--ENDREGION");
        return sb.ToString();
    }
}
