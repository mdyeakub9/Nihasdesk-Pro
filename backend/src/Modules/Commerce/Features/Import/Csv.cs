using System.Text;

namespace Nihasdesk.Modules.Commerce.Features.Import;

/// <summary>
/// RFC 4180 CSV reading and writing, enough for product files (ADR 0018). Every cell is data,
/// never a formula or markup (SEC-04, SEC-23). Comma or semicolon (Excel in Italy) is detected
/// from the header line.
/// </summary>
internal static class Csv
{
    /// <summary>Rows of cells; stops (and reports) once <paramref name="maxRows"/> data rows are exceeded.</summary>
    public static (List<string[]> Rows, bool TooMany) Read(string text, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(text);
        var delimiter = DetectDelimiter(text);
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                cells.Add(cell.ToString());
                cell.Clear();
                if (cells.Count > 1 || cells[0].Length > 0)
                {
                    rows.Add([.. cells]);
                    if (rows.Count > maxRows + 1)
                    {
                        return (rows, true);
                    }
                }

                cells.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        if (cells.Count > 1 || cells[0].Length > 0)
        {
            rows.Add([.. cells]);
        }

        return (rows, rows.Count > maxRows + 1);
    }

    /// <summary>A CSV line; cells that a spreadsheet would run as a formula get a leading <c>'</c> (SEC-23).</summary>
    public static string Line(IEnumerable<string?> cells) =>
        string.Join(',', cells.Select(cell =>
        {
            var value = cell ?? string.Empty;
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                value = "'" + value;
            }

            return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
        }));

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }
}
