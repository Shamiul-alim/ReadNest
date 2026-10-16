using System.Text;

namespace ReadNest.Application.ImportExport;

public sealed class CsvFormatException(string message) : Exception(message);

/// <summary>Minimal RFC 4180 CSV reader with hard limits, for untrusted uploads.</summary>
public static class CsvReader
{
    public const int MaxFieldLength = 10_000;
    public const int MaxColumns = 50;

    /// <summary>Parses all records. Throws <see cref="CsvFormatException"/> for malformed input or exceeded limits.</summary>
    public static List<string[]> Parse(string content, int maxRecords)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        var i = 0;

        // Skip a UTF-8 byte order mark.
        if (content.Length > 0 && content[0] == '﻿')
        {
            i = 1;
        }

        void EndField()
        {
            if (fields.Count >= MaxColumns)
            {
                throw new CsvFormatException($"Too many columns (maximum {MaxColumns}).");
            }

            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();
            if (!(fields.Count == 1 && fields[0].Length == 0))
            {
                if (records.Count >= maxRecords)
                {
                    throw new CsvFormatException($"Too many rows (maximum {maxRecords - 1} data rows).");
                }

                records.Add(fields.ToArray());
            }

            fields.Clear();
        }

        for (; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                if (fieldStarted && field.Length > 0)
                {
                    throw new CsvFormatException($"Unexpected quote inside an unquoted field (record {records.Count + 1}).");
                }

                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == ',')
            {
                EndField();
            }
            else if (c == '\r')
            {
                // Handled together with the following \n (or as a lone line break).
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else if (c == '\n')
            {
                EndRecord();
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }

            if (field.Length > MaxFieldLength)
            {
                throw new CsvFormatException($"A value is longer than {MaxFieldLength} characters (record {records.Count + 1}).");
            }
        }

        if (inQuotes)
        {
            throw new CsvFormatException("The file ends inside a quoted value.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
}

public static class CsvWriter
{
    /// <summary>
    /// Escapes a value for CSV. Values starting with =, +, -, @, tab or carriage return are prefixed with an
    /// apostrophe so spreadsheet applications do not evaluate them as formulas (CSV injection).
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuotes = value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || value[0] == ' ' || value[^1] == ' ' || value[0] == '\'';
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    public static string Line(IEnumerable<string?> values) => string.Join(',', values.Select(Escape));
}
