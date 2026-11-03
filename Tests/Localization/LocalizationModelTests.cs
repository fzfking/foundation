using System;
using System.Collections.Generic;
using Foundation.Localization;
using NUnit.Framework;

namespace Foundation.Tests.Localization
{
    public sealed class LocalizationModelTests
    {
        [Test]
        public void LocaleId_ComparesCaseInsensitivelyAndExposesItsLanguage()
        {
            Assert.That(new LocaleId("ru-RU"), Is.EqualTo(new LocaleId("RU-ru")));
            Assert.That(new LocaleId("ru-RU").Language, Is.EqualTo(new LocaleId("ru")));
            Assert.That(new LocaleId("ru").Language, Is.EqualTo(new LocaleId("ru")));
            Assert.That(new LocaleId("ru-RU").HasRegion, Is.True);
            Assert.That(new LocaleId("ru").HasRegion, Is.False);
        }

        [Test]
        public void Settings_RejectDuplicateLocalesAndAnUnsupportedFallback()
        {
            Assert.Throws<ArgumentException>(() => new LocalizationSettings(
                new[] { new LocaleId("en"), new LocaleId("EN") },
                new LocaleId("en")));

            Assert.Throws<ArgumentException>(() => new LocalizationSettings(
                new[] { new LocaleId("en") },
                new LocaleId("ru")));
        }

        [Test]
        public void Settings_ResolveTheClosestSupportedLocale()
        {
            var settings = new LocalizationSettings(
                new[] { new LocaleId("en"), new LocaleId("pt-BR") },
                new LocaleId("en"));

            Assert.That(settings.TryResolveSupported(new LocaleId("en"), out var exact), Is.True);
            Assert.That(exact, Is.EqualTo(new LocaleId("en")));

            Assert.That(settings.TryResolveSupported(new LocaleId("pt"), out var byLanguage), Is.True);
            Assert.That(byLanguage, Is.EqualTo(new LocaleId("pt-BR")));

            Assert.That(settings.TryResolveSupported(new LocaleId("de"), out _), Is.False);
        }

        [Test]
        public void Table_RoundTripsThroughItsSerializedForm()
        {
            var data = new LocalizationTableData
            {
                Locale = "en",
                Keys = new[] { "a", "b" },
                Values = new[] { "A", "B" }
            };

            var table = LocalizationTable.FromData(data);

            Assert.That(table.Locale, Is.EqualTo(new LocaleId("en")));
            Assert.That(table.TryGet("a", out var value), Is.True);
            Assert.That(value, Is.EqualTo("A"));
            Assert.That(table.TryGet("missing", out _), Is.False);
        }

        [Test]
        public void Table_RejectsMalformedPayloads()
        {
            Assert.Throws<InvalidOperationException>(() => LocalizationTable.FromData(new LocalizationTableData
            {
                Locale = "en",
                Keys = new[] { "a", "b" },
                Values = new[] { "A" }
            }));

            Assert.Throws<InvalidOperationException>(() => LocalizationTable.FromData(new LocalizationTableData
            {
                Locale = "en",
                Keys = new[] { "a", "a" },
                Values = new[] { "A", "B" }
            }));

            Assert.Throws<InvalidOperationException>(() => LocalizationTable.FromData(new LocalizationTableData
            {
                Locale = " ",
                Keys = Array.Empty<string>(),
                Values = Array.Empty<string>()
            }));
        }

        [Test]
        public void Table_LooksUpByOrdinalKey()
        {
            var table = new LocalizationTable(
                new LocaleId("en"),
                new Dictionary<string, string> { ["Common.Yes"] = "Yes" });

            Assert.That(table.TryGet("Common.Yes", out _), Is.True);
            Assert.That(table.TryGet("common.yes", out _), Is.False, "keys are case sensitive on purpose");
        }
    }
}
