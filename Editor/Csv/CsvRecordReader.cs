using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Foundation.Content.Editor
{
    /// <summary>
    /// Reads RFC 4180 CSV with a header row: quoted fields may hold commas, quotes (doubled) and line breaks; records end
    /// with <c>\n</c>, <c>\r\n</c> or <c>\r</c>. A short record reads as empty cells; records wider than the header and
    /// malformed quoting are listed in <see cref="Errors"/>. <see cref="RowNumber"/> counts like a spreadsheet does: the
    /// header is row 1, the first record row 2. Malformed text is still read as far as the input allows.
    /// </summary>
    internal sealed class CsvRecordReader : IDisposable
    {
        private readonly TextReader _reader;
        private readonly string[] _header;
        private readonly List<string> _errors = new();
        private string[] _record = Array.Empty<string>();

        public int FieldCount => _header.Length;

        public int RowNumber { get; private set; }

        public IReadOnlyList<string> Errors => _errors;

        private CsvRecordReader(TextReader reader)
        {
            _reader = reader;
            _header = ReadRecord() ?? Array.Empty<string>();
        }

        public static CsvRecordReader Create(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            return new CsvRecordReader(reader);
        }

        public string GetName(int ordinal) => _header[ordinal];

        public string GetString(int ordinal) => ordinal < _record.Length ? _record[ordinal] : string.Empty;

        public bool Read()
        {
            var record = ReadRecord();
            if (record == null)
                return false;

            if (record.Length > _header.Length)
                _errors.Add($"row {RowNumber}: record has {record.Length} fields, but the header has {_header.Length}.");

            _record = record;
            return true;
        }

        public void Dispose() => _reader.Dispose();

        private string[] ReadRecord()
        {
            if (_reader.Peek() < 0)
                return null;

            RowNumber++;
            var fields = new List<string>();
            var field = new StringBuilder();
            while (true)
            {
                field.Clear();
                var quoted = _reader.Peek() == '"';
                if (quoted)
                {
                    _reader.Read();
                    if (!ReadQuoted(field))
                        _errors.Add($"row {RowNumber}: a quoted field is never closed.");
                    else if (!IsFieldEnd(_reader.Peek()))
                        _errors.Add($"row {RowNumber}: text follows a closing quote.");
                }

                int next;
                while (!IsFieldEnd(next = _reader.Peek()))
                    field.Append((char)_reader.Read());

                fields.Add(field.ToString());
                if (next == ',')
                {
                    _reader.Read();
                    continue;
                }

                if (next == '\r')
                {
                    _reader.Read();
                    if (_reader.Peek() == '\n')
                        _reader.Read();
                }
                else if (next == '\n')
                {
                    _reader.Read();
                }

                return fields.ToArray();
            }
        }

        // Returns false when the input ends before the closing quote.
        private bool ReadQuoted(StringBuilder field)
        {
            int next;
            while ((next = _reader.Read()) >= 0)
            {
                if (next != '"')
                {
                    field.Append((char)next);
                    continue;
                }

                if (_reader.Peek() != '"')
                    return true;

                _reader.Read();
                field.Append('"');
            }

            return false;
        }

        private static bool IsFieldEnd(int next) => next < 0 || next == ',' || next == '\n' || next == '\r';
    }
}
