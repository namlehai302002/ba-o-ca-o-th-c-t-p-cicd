using System.Globalization;
using System.Text;

namespace WMS.Common;

public static class SpreadsheetExportSecurity
{
    public const int MaxSynchronousRows = 5000;

    public static byte[] EncodeUtf8Csv(string? content)
    {
        var body = Encoding.UTF8.GetBytes(content ?? string.Empty);
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    public static string EscapeCsv(string? value, bool alwaysQuote = false)
    {
        var safeValue = NeutralizeFormula(value);
        if (alwaysQuote
            || safeValue.Contains('"')
            || safeValue.Contains(',')
            || safeValue.Contains('\n')
            || safeValue.Contains('\r'))
        {
            return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
        }

        return safeValue;
    }

    public static string NeutralizeFormula(string? value)
    {
        var text = value ?? string.Empty;
        var candidate = text.TrimStart(' ', '\t', '\r', '\n');
        if (candidate.Length == 0)
            return text;

        var first = candidate[0];
        if (first is not ('=' or '+' or '-' or '@'))
            return text;

        if (first is '+' or '-'
            && decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return text;
        }

        return "'" + text;
    }
}
