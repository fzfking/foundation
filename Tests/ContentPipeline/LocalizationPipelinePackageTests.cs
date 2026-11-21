using System.Linq;
using System;
using System.IO;
using Foundation.Content.Editor.Localization;
using Foundation.Localization;
using Foundation.Serialization;
using NUnit.Framework;
using UnityEditor;

namespace Foundation.Content.Editor.PackageTests
{
    public sealed class LocalizationPipelinePackageTests
    {
        private const string Csv = "Key,Context,ru,en\na.greeting,,Привет,Hello\n";

        [TestCase(false)]
        [TestCase(true)]
        public void BuildAdapters_ReadConfiguredCsvAndImportGeneratedScripts(bool absoluteOutputPath)
        {
            var name = "FoundationLocalizationTest" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            var folder = "Assets/" + name;
            try
            {
                var output = folder + "/Generated";
                var settings = new LocalizationBuildSettings
                {
                    CsvPath = folder + "/translations.csv",
                    KeysOutputDirectory = absoluteOutputPath ? Path.GetFullPath(output) : output,
                    KeysNamespace = "Tests.Generated.Localization"
                };
                File.WriteAllText(settings.CsvPath, Csv);

                var sources = new LocalizationCsvSourceProvider(settings).Provide().ToArray();
                Assert.That(sources.Single(), Is.TypeOf<LocalizationCsvSource>());

                var builder = CreateBuilder(settings, folder + "/Binaries");
                Assert.That(builder.Build(sources).Success, Is.True);

                var keys = output + "/" + LocalizationKeysGenerator.KeysFileName;
                var locales = output + "/" + LocalizationKeysGenerator.LocalesFileName;
                Assert.That(File.ReadAllText(keys), Does.Contain("namespace Tests.Generated.Localization"));
                Assert.That(File.ReadAllText(keys), Does.Contain("A_Greeting = \"a.greeting\""));
                Assert.That(File.ReadAllText(locales), Does.Contain("\"ru\"").And.Contain("\"en\""));
                Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(keys), Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(locales), Is.Not.Null);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void MissingRequiredCsv_StopsSourceDiscovery()
        {
            var settings = new LocalizationBuildSettings
            {
                CsvPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv")
            };
            var provider = new LocalizationCsvSourceProvider(settings);

            var failure = Assert.Throws<FileNotFoundException>(() => provider.Provide().ToArray());

            Assert.That(failure.FileName, Is.EqualTo(settings.CsvPath));
        }

        [TestCase(null, "Namespace must not be empty")]
        [TestCase("Game..Localization", "valid C# identifiers")]
        [TestCase("Game.namespace", "valid C# identifiers")]
        public void ValidateAndBuild_ReportInvalidNamespaceWithoutWritingFiles(string namespaceName, string error)
        {
            var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var settings = new LocalizationBuildSettings
            {
                KeysOutputDirectory = Path.Combine(output, "Scripts"),
                KeysNamespace = namespaceName,
            };
            var builder = CreateBuilder(settings, Path.Combine(output, "Binaries"));
            var sources = new[] { new LocalizationCsvSource(LocalizationCsvReader.ReadText(Csv)) };

            var validation = builder.Validate(sources);
            var build = builder.Build(sources);

            foreach (var report in new[] { validation, build })
            {
                Assert.That(report.Success, Is.False);
                Assert.That(report.Errors.Single().Kind, Is.EqualTo(ConfigBuildErrorKind.Invalid));
                Assert.That(report.Errors.Single().Message, Does.Contain(error));
            }

            Assert.That(Directory.Exists(output), Is.False);
        }

        [Test]
        public void ValidateAndBuild_RejectConflictingIdentifiersWithoutWritingFiles()
        {
            var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var settings = new LocalizationBuildSettings
            {
                KeysOutputDirectory = Path.Combine(output, "Scripts"),
                KeysNamespace = "Game.Localization",
            };
            var builder = CreateBuilder(settings, Path.Combine(output, "Binaries"));
            var document = LocalizationCsvReader.ReadText("Key,Context,ru,en\nui.ok,,Да,OK\nui-ok,,Нет,No\n");
            var sources = new[] { new LocalizationCsvSource(document) };

            foreach (var report in new[] { builder.Validate(sources), builder.Build(sources) })
            {
                Assert.That(report.Success, Is.False);
                Assert.That(report.Errors.Single().Message,
                    Does.Contain("'ui.ok'").And.Contain("'ui-ok'").And.Contain("'Ui_Ok'"));
            }

            Assert.That(Directory.Exists(output), Is.False);
        }

        [Test]
        public void WritingSecondScriptFails_RestoresKeysAndBinaryTables()
        {
            var folder = Path.Combine(Path.GetTempPath(), "foundation-localization-" + Guid.NewGuid().ToString("N"));
            var scripts = Path.Combine(folder, "Generated");
            var binaries = Path.Combine(folder, "Binaries");
            try
            {
                var keys = Path.Combine(scripts, LocalizationKeysGenerator.KeysFileName);
                Directory.CreateDirectory(Path.Combine(scripts, LocalizationKeysGenerator.LocalesFileName));
                File.WriteAllText(keys, "previous keys");
                Directory.CreateDirectory(binaries);
                var path = Path.Combine(binaries, LocalizationCsvExporter.FileNameFor(new LocaleId("ru")));
                File.WriteAllBytes(path, new byte[] { 7, 8 });

                var settings = new LocalizationBuildSettings { KeysOutputDirectory = scripts, KeysNamespace = "Tests.Generated" };
                var builder = CreateBuilder(settings, binaries);
                var source = new LocalizationCsvSource(LocalizationCsvReader.ReadText(Csv));

                Assert.Throws<IOException>(() => builder.Build(new[] { source }));

                Assert.That(File.ReadAllText(keys), Is.EqualTo("previous keys"));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 7, 8 }));
                Assert.That(Directory.GetFiles(binaries), Is.EqualTo(new[] { path }));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static ConfigBuilder CreateBuilder(LocalizationBuildSettings settings, string binaries)
        {
            var registry = new ConfigBuildRegistry();
            registry.RegisterExporter(new LocalizationCsvExporter());
            registry.RegisterPostProcessor(new LocalizationCodeGenerator(settings));
            return new ConfigBuilder(new ConfigBuildSettings
            {
                OutputDirectory = binaries,
                Serializer = new TestSerializer()
            }, registry);
        }

        private sealed class TestSerializer : ISerializer
        {
            public byte[] Serialize<T>(T value) => new byte[] { 3, 4 };
            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException();
            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException();
        }

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
