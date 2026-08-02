using JustyBase.Models;
using System.Globalization;
using System.Text;

namespace JustyBase.Helpers;

internal sealed class CopyHtmlOrTextClipboard
{

    private static readonly (string, string)[] _valuesToReplace =
    [
        ("&", "&amp;"),
        ("<", "&lt;"),
        (">", "&gt;"),
        ("\"", "&quot;"),
        ("'", "&apos;")
    ];

    private static string GetEscapedText(string txt)
    {
        if (txt is null)
        {
            return "";
        }

        foreach (var (oldValue, newValue) in _valuesToReplace)
        {
            if (txt.Contains(oldValue, StringComparison.Ordinal))
            {
                txt = txt.Replace(oldValue, newValue);
            }
        }
        return txt;
    }


    private static string TableToHtml(TableOfSqlResults table)
    {
        StringBuilder sb = new();
        sb.Append("<table style=\"border: 2px solid black; background-color: rgb(220, 220, 220)\">");

        sb.Append("<tr>");
        for (int j = 0; j < table.Headers.Count; j++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<th>{GetEscapedText(table.Headers[j])}</th>");
        }
        sb.Append("</tr>");
        for (int i = 0; i < table.Rows.Count; i++)
        {
            var fields = table.Rows[i].Fields;
            sb.Append("<tr>");
            for (int j = 0; j < table.Headers.Count; j++)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<td>{GetEscapedText(fields[j]?.ToString())}</td>");
            }
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
    

    public static byte[] GetHtmlBytes(string htmlCode)
    {
        int htmlLenInBytex = Encoding.UTF8.GetBytes(htmlCode).Length;

        //int htmlLen = _htmlCode.Length;
        int StartHTML = 97;
        int StartFragment = 133;
        int EndHTML = htmlLenInBytex + StartHTML + 73;
        int EndFragment = StartFragment + htmlLenInBytex + 2;
        var str = $"""
            Version:0.9
            StartHTML:{StartHTML:00000000}
            EndHTML:{EndHTML:00000000}
            StartFragment:{StartFragment:00000000}
            EndFragment:{EndFragment:00000000}
            <html><body><!--StartFragment-->{htmlCode}<!--EndFragment--></body></html>
            """;
        return Encoding.UTF8.GetBytes(str);
    }

    public static byte[] GetHtmlBytesOfTable(TableOfSqlResults table)
    {
        string _htmlCode = TableToHtml(table);
        return GetHtmlBytes(_htmlCode);
    }
}
