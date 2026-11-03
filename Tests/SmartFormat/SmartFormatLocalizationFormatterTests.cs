using System.Collections.Generic;
using System.Globalization;
using Foundation.DI;
using Foundation.Localization;
using NUnit.Framework;
using SmartFormat.Core.Parsing;

namespace Foundation.Infrastructure.SmartFormat.Tests
{
    public sealed class SmartFormatLocalizationFormatterTests
    {
        private const string Items = "{count:plural:{} предмет|{} предмета|{} предметов}";

        [TestCase(1, "1 предмет")]
        [TestCase(3, "3 предмета")]
        [TestCase(5, "5 предметов")]
        [TestCase(21, "21 предмет")]
        public void Plurals_FollowTheCultureTheServiceSet(int count, string expected)
        {
            var formatter = new SmartFormatLocalizationFormatter();
            formatter.SetCulture(CultureInfo.GetCultureInfo("ru"));

            Assert.That(formatter.Format(Items, Args(("count", count))), Is.EqualTo(expected));
        }

        [Test]
        public void TheSameTemplate_ReadsDifferentlyWhenTheCultureChanges()
        {
            var formatter = new SmartFormatLocalizationFormatter();
            const string template = "{count:plural:{} item|{} items}";

            formatter.SetCulture(CultureInfo.GetCultureInfo("en"));
            Assert.That(formatter.Format(template, Args(("count", 5))), Is.EqualTo("5 items"));

            formatter.SetCulture(CultureInfo.GetCultureInfo("fr"));
            Assert.That(formatter.Format(template, Args(("count", 0))), Is.EqualTo("0 item"), "French treats zero as singular");
        }

        [Test]
        public void AMalformedTemplate_FailsLoudlyInsteadOfShippingBrokenText()
        {
            var formatter = new SmartFormatLocalizationFormatter();

            Assert.Throws<ParsingErrors>(
                () => formatter.Format("Hello {name", Args(("name", "x"))));
        }

        [Test]
        public void UseSmartFormat_ProvidesTheFormatterTheLocalizationServiceNeeds_AndAppliesTheConfiguration()
        {
            var configured = 0;
            using var scope = MinimalServiceScope.Create(registry => registry.UseSmartFormat(_ => configured++));

            var formatter = scope.Resolve<ILocalizationFormatter>();

            Assert.That(formatter, Is.InstanceOf<SmartFormatLocalizationFormatter>());
            Assert.That(formatter.Format("Hello {name}", Args(("name", "Vlad"))), Is.EqualTo("Hello Vlad"));
            Assert.That(configured, Is.EqualTo(1));
        }

        private static IReadOnlyDictionary<string, object> Args(params (string Name, object Value)[] values)
        {
            var result = new Dictionary<string, object>();
            foreach (var (name, value) in values)
                result[name] = value;
            return result;
        }
    }
}
