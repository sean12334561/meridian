using System.Text;

namespace Meridian.Api.Features.Imports.Parsing;

public class CsvRow
{
    public required int LineNo { get; init; }
    public required string RawText { get; init; }

    /// <summary>Null when the line's quotes are broken.</summary>
    public required string[]? Fields { get; init; }
}

/// <summary>
/// Reads a CSV stream one line at a time, and each line is one row, so a malformed row becomes data
/// and not an exception. A CSV parser would treat a stray quote as a field spanning line breaks and
/// silently swallow the rows after it. Only a problem with the whole file throws.
/// </summary>
public class CsvRowReader : IDisposable
{
    private const char Quote = '"';
    private const char Comma = ',';

    private readonly StreamReader _reader;
    private int _lineNo;

    public CsvRowReader(Stream stream)
    {
        // Strict UTF-8: a bad byte throws. The encoding's preamble makes the reader skip a BOM
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        _reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false);
    }

    public async Task<CsvColumns> ReadColumnsAsync()
    {
        CsvRow? header = await ReadRowAsync();

        return CsvColumns.FromHeader(header?.Fields ?? []);
    }

    public async Task<CsvRow?> ReadRowAsync()
    {
        string? line;
        do
        {
            line = await ReadLineAsync();
            if (line is null)
            {
                return null;
            }

            _lineNo++;
        }
        while (line.Length == 0);

        if (line.Contains('\0'))
        {
            throw new ImportFileException($"Line {_lineNo} contains a NUL character, so the file isn't text.");
        }

        return new CsvRow { LineNo = _lineNo, RawText = line, Fields = SplitFields(line) };
    }

    public void Dispose()
    {
        _reader.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string?> ReadLineAsync()
    {
        try
        {
            return await _reader.ReadLineAsync();
        }
        catch (DecoderFallbackException)
        {
            throw new ImportFileException("The file is not valid UTF-8.");
        }
    }

    private static string[]? SplitFields(string line)
    {
        List<string> fields = new(CsvColumns.Count);
        int position = 0;

        while (true)
        {
            string? field = position < line.Length && line[position] == Quote
                ? ReadQuotedField(line, ref position)
                : ReadPlainField(line, ref position);

            if (field is null)
            {
                return null;
            }

            fields.Add(field);

            if (position >= line.Length)
            {
                return fields.ToArray();
            }

            position++;     // the comma
        }
    }

    private static string? ReadPlainField(string line, ref int position)
    {
        int end = line.IndexOf(Comma, position);
        if (end < 0)
        {
            end = line.Length;
        }

        string field = line[position..end];
        position = end;

        return field.Contains(Quote) ? null : field;
    }

    private static string? ReadQuotedField(string line, ref int position)
    {
        StringBuilder field = new();
        int start = position + 1;

        while (true)
        {
            int end = line.IndexOf(Quote, start);
            if (end < 0)
            {
                return null;
            }

            bool isEscapedQuote = end + 1 < line.Length && line[end + 1] == Quote;
            if (isEscapedQuote)
            {
                field.Append(line, start, end - start + 1);
                start = end + 2;
                continue;
            }

            field.Append(line, start, end - start);
            position = end + 1;

            return position >= line.Length || line[position] == Comma ? field.ToString() : null;
        }
    }
}
