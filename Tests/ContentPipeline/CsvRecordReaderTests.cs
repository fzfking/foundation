using System.IO;
using NUnit.Framework;

namespace Foundation.Content.Editor.PackageTests
{
    public sealed class CsvRecordReaderTests
    {
        [Test]
        public void QuotedFields_KeepCommasQuotesAndLineBreaksInsideTheCell()
        {
            const string csv = "Key,en\nk,\"Hello, \"\"world\"\"\nsecond line\"\nnext,plain\n";
            using var reader = CsvRecordReader.Create(new StringReader(csv));

            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("k"));
            Assert.That(reader.GetString(1), Is.EqualTo("Hello, \"world\"\nsecond line"));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(1), Is.EqualTo("plain"));
            Assert.That(reader.Read(), Is.False, "the final line break does not start another record");
            Assert.That(reader.Errors, Is.Empty);
        }

        [Test]
        public void UnclosedQuote_IsReportedAtItsRow_InsteadOfSwallowingTheRest()
        {
            using var reader = CsvRecordReader.Create(new StringReader("Key,en\nk,\"never closed\nnext,plain\n"));

            reader.Read();

            Assert.That(reader.Errors, Has.Count.EqualTo(1));
            Assert.That(reader.Errors[0], Does.Contain("row 2"));
        }

        [Test]
        public void TextAfterAClosingQuote_IsReportedAndStillKept()
        {
            using var reader = CsvRecordReader.Create(new StringReader("Key,en\nk,\"abc\"def\n"));

            reader.Read();

            Assert.That(reader.GetString(1), Is.EqualTo("abcdef"));
            Assert.That(reader.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void ShortRowsReadAsEmptyCells_AndWindowsLineEndingsAreOneBreak()
        {
            using var reader = CsvRecordReader.Create(new StringReader("Key,Context,en\r\nonly-key\r\na,b,c"));

            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetName(2), Is.EqualTo("en"));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("only-key"));
            Assert.That(reader.GetString(2), Is.Empty);
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(2), Is.EqualTo("c"));
            Assert.That(reader.Read(), Is.False);
        }

        [Test]
        public void RowsWithMoreFieldsThanTheHeader_AreReportedAsInvalid()
        {
            using var reader = CsvRecordReader.Create(new StringReader("Key,en\nhello,Hello,unexpected\n"));

            Assert.That(reader.Read(), Is.True);

            Assert.That(reader.Errors, Has.Count.EqualTo(1));
            Assert.That(reader.Errors[0], Does.Contain("row 2"));
            Assert.That(reader.Errors[0], Does.Contain("3 fields"));
        }

        [Test]
        public void RowNumbers_MatchTheRowsOfTheSpreadsheetTheTranslatorSees()
        {
            using var reader = CsvRecordReader.Create(new StringReader("Key,en\nfirst,\"two\nlines\"\nsecond,x\n"));

            reader.Read();
            Assert.That(reader.RowNumber, Is.EqualTo(2), "the header is row 1");
            reader.Read();
            Assert.That(reader.RowNumber, Is.EqualTo(3), "a multi-line cell is still one row");
        }

        [Test]
        public void AnEmptyFile_HasNoColumnsAndNoRows()
        {
            using var reader = CsvRecordReader.Create(new StringReader(string.Empty));

            Assert.That(reader.FieldCount, Is.Zero);
            Assert.That(reader.Read(), Is.False);
        }
    }
}
