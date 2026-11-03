using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Localization;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Localization
{
    public sealed class LocalizationResolverTests
    {
        [Test]
        public async Task ResolveWithoutArguments_ReturnsTemplateWithoutFormatting()
        {
            var formatter = new RecordingFormatter();
            var resolver = new LocalizationResolver(new StubSource("Hello"), formatter);

            var result = await resolver.ResolveAsync("hello");

            Assert.That(result, Is.EqualTo("Hello"));
            Assert.That(formatter.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ResolveWithArguments_FormatsTemplate()
        {
            var formatter = new RecordingFormatter();
            var resolver = new LocalizationResolver(new StubSource("Hello {name}"), formatter);

            var result = await resolver.ResolveAsync(
                "hello",
                new LocalizationArguments(new Dictionary<string, object> { ["name"] = "Ada" }));

            Assert.That(result, Is.EqualTo("formatted"));
            Assert.That(formatter.CallCount, Is.EqualTo(1));
        }

        private sealed class StubSource : ILocalizationSource
        {
            private readonly string _template;

            public StubSource(string template)
            {
                _template = template;
            }

            public UniTask<string> LoadTemplateAsync(string key, CancellationToken cancellationToken = default)
                => UniTask.FromResult(_template);
        }

        private sealed class RecordingFormatter : ILocalizationFormatter
        {
            public int CallCount { get; private set; }

            public string Format(string template, IReadOnlyDictionary<string, object> arguments)
            {
                CallCount++;
                return "formatted";
            }
        }
    }
}
