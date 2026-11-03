using System.Linq;
using System;
using Foundation.Content.Editor.Localization;
using Foundation.Localization;
using NUnit.Framework;

namespace Foundation.Content.Editor.PackageTests
{
    public sealed class LocalizationPipelinePackageTests
    {
        private const string Csv = "Key,Context,ru,en\na.greeting,,Привет,Hello\n";

        [Test]
        public void Reader_ParsesLocalesAndRows()
        {
            var document = LocalizationCsvReader.ReadText(Csv);

            Assert.That(document.Locales.Select(l => l.Value), Is.EqualTo(new[] { "ru", "en" }));
            Assert.That(document.Rows.Single().Key, Is.EqualTo("a.greeting"));
        }

        [Test]
        public void Reader_UnclosedQuote_IsReportedAsAnErrorOfTheSheet()
        {
            var document = LocalizationCsvReader.ReadText("Key,Context,en\na.open,,\"Hello\nb.next,,World\n");

            Assert.That(document.HeaderErrors, Has.Some.Contains("row 2: a quoted field is never closed."));
        }

        [TestCase("Key,key,Context,en\nfirst,ignored,,Hello\n", "Key")]
        [TestCase("Key,Context,context,en\na.greeting,first,ignored,Hello\n", "Context")]
        public void DuplicateReservedHeader_IsReportedAndBlocksValidation(string csv, string reservedHeader)
        {
            var document = LocalizationCsvReader.ReadText(csv);
            var errors = new LocalizationCsvValidator(new LocaleId("en"))
                .Validate(new LocalizationCsvSource(document), Array.Empty<ConfigExportGroup>())
                .ToArray();

            Assert.That(document.HeaderErrors, Has.Some.Contains($"reserved '{reservedHeader}' column"));
            Assert.That(errors, Is.Not.Empty);
        }

        [Test]
        public void Validator_WithSuppliedFallback_PassesCleanSheet()
        {
            var document = LocalizationCsvReader.ReadText(Csv);
            var validator = new LocalizationCsvValidator(new LocaleId("ru"));

            var errors = validator
                .Validate(new LocalizationCsvSource(document), Array.Empty<ConfigExportGroup>())
                .ToArray();

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void Validator_MissingCanonicalLocaleColumn_Fails()
        {
            var document = LocalizationCsvReader.ReadText(Csv);
            var validator = new LocalizationCsvValidator(new LocaleId("de"));

            var errors = validator
                .Validate(new LocalizationCsvSource(document), Array.Empty<ConfigExportGroup>())
                .ToArray();

            Assert.That(errors, Is.Not.Empty);
        }

        [Test]
        public void Exporter_WritesOneGroupPerLocale()
        {
            var document = LocalizationCsvReader.ReadText(Csv);
            var groups = new LocalizationCsvExporter().Export(new LocalizationCsvSource(document));

            Assert.That(groups.Select(g => g.FileName), Is.EquivalentTo(new[]
            {
                "localization_ru.bytes",
                "localization_en.bytes"
            }));
        }

        [Test]
        public void KeysGenerator_UsesSuppliedNamespaceAndSourceDescription()
        {
            var document = LocalizationCsvReader.ReadText(Csv);

            var source = LocalizationKeysGenerator.GenerateKeysSource(
                document.Rows.Select(r => r.Key), "My.Custom.Namespace", "my-sheet.csv");

            Assert.That(source, Does.Contain("namespace My.Custom.Namespace"));
            Assert.That(source, Does.Contain("Generated from my-sheet.csv"));
            Assert.That(source, Does.Contain("A_Greeting"));
        }

        [Test]
        public void GeneratedKeys_ALineBreakInAKey_CannotEndTheStringLiteral()
        {
            var source = LocalizationKeysGenerator.GenerateKeysSource(
                new[] { "ok\nstatic int Injected;//", "LocalizationKeys" }, "Game", "sheet.csv");

            Assert.That(source, Does.Not.Contain("\nstatic int Injected"));
            Assert.That(source, Does.Contain("ok\\u000astatic int Injected;//"));
            Assert.That(source, Does.Contain("LocalizationKeys_2 = \"LocalizationKeys\""),
                "a key named like the class gets another member name");
        }
    }
}
