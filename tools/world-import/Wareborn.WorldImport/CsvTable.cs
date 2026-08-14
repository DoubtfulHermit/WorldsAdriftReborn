using System.Text;

namespace Wareborn.WorldImport;

internal sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    private CsvTable(string path, IReadOnlyList<string> headers, IReadOnlyList<Row> rows)
    {
        Path = path;
        Headers = headers;
        Rows = rows;
        _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            string header = headers[i].Trim().TrimStart('\uFEFF');
            if (!_columns.TryAdd(header, i))
            {
                throw new WAMapValidationException(
                    $"{path}: duplicate CSV column '{header}'.");
            }
        }
    }

    public string Path { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<Row> Rows { get; }

    public static CsvTable Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new WAMapValidationException($"Required WAMap file is missing: {path}");
        }

        List<IReadOnlyList<string>> records = Parse(File.ReadAllText(path));
        if (records.Count == 0)
        {
            throw new WAMapValidationException($"{path}: CSV is empty.");
        }

        IReadOnlyList<string> headers = records[0];
        List<Row> rows = new();
        for (int i = 1; i < records.Count; i++)
        {
            IReadOnlyList<string> values = records[i];
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }
            if (values.Count != headers.Count)
            {
                throw new WAMapValidationException(
                    $"{path}:{i + 1}: expected {headers.Count} fields, found {values.Count}.");
            }
            rows.Add(new Row(i + 1, values));
        }
        return new CsvTable(path, headers, rows);
    }

    public int RequireColumn(string name)
    {
        if (!_columns.TryGetValue(name, out int index))
        {
            throw new WAMapValidationException(
                $"{Path}: required CSV column '{name}' is missing.");
        }
        return index;
    }

    public int? OptionalColumn(string name) =>
        _columns.TryGetValue(name, out int index) ? index : null;

    private static List<IReadOnlyList<string>> Parse(string text)
    {
        List<IReadOnlyList<string>> records = new();
        List<string> record = new();
        StringBuilder field = new();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    if (field.Length != 0)
                    {
                        throw new WAMapValidationException(
                            "CSV quote appeared after unquoted field content.");
                    }
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record.ToArray());
                    record.Clear();
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record.ToArray());
                    record.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (quoted)
        {
            throw new WAMapValidationException("CSV ended inside a quoted field.");
        }
        if (field.Length != 0 || record.Count != 0)
        {
            record.Add(field.ToString());
            records.Add(record.ToArray());
        }
        return records;
    }

    internal sealed record Row(int LineNumber, IReadOnlyList<string> Values)
    {
        public string At(int index) => Values[index].Trim();
    }
}
