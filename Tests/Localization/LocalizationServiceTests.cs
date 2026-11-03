using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Foundation.Localization;
using Foundation.Logging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.Localization
{
    public sealed class LocalizationServiceTests
    {
        private static readonly LocaleId En = new("en");
        private static readonly LocaleId Ru = new("ru");
        private static readonly LocaleId RuRu = new("ru-RU");

        [Test]
        public void ExactLocale_IsUsedWhenAvailable()
        {
            var service = Create(out _);
            Initialize(service, Ru);

            Assert.That(service.CurrentLocale, Is.EqualTo(Ru));
            Assert.That(service.GetTemplate("greeting"), Is.EqualTo("Привет"));
        }

        [Test]
        public void RegionalLocale_FallsBackToItsLanguage()
        {
            var service = Create(out _);
            Initialize(service, RuRu);

            Assert.That(service.CurrentLocale, Is.EqualTo(Ru), "ru-RU is served by the supported ru table");
            Assert.That(service.GetTemplate("greeting"), Is.EqualTo("Привет"));
        }

        [Test]
        public void KeyMissingInTheCurrentLocale_FallsBackToTheFallbackLocale()
        {
            var service = Create(out _);
            Initialize(service, Ru);

            Assert.That(service.GetTemplate("only-english"), Is.EqualTo("English only"));
        }

        [Test]
        public void UnknownKey_ReturnsALoudMarkerRatherThanEmptyText()
        {
            var service = Create(out _);
            Initialize(service, Ru);

            Assert.That(service.GetTemplate("nope"), Is.EqualTo("[MISSING:nope]"));
        }

        [Test]
        public void SwitchingLocale_LoadsTheTableAndNotifiesSubscribers()
        {
            var service = Create(out _);
            Initialize(service, En);
            var observed = new List<LocaleId>();
            using var subscription = service.Locale.Subscribe(observed.Add);

            var switched = service.SetLocaleAsync(Ru).GetAwaiter().GetResult();

            Assert.That(switched, Is.True);
            Assert.That(service.GetTemplate("greeting"), Is.EqualTo("Привет"));
            Assert.That(observed, Is.EqualTo(new[] { En, Ru }), "subscribers get the current value, then changes");
        }

        [Test]
        public void UnsupportedLocale_IsRejectedAndTheCurrentOneKeepsWorking()
        {
            var service = Create(out _);
            Initialize(service, Ru);

            var switched = service.SetLocaleAsync(new LocaleId("de")).GetAwaiter().GetResult();

            Assert.That(switched, Is.False);
            Assert.That(service.CurrentLocale, Is.EqualTo(Ru));
            Assert.That(service.GetTemplate("greeting"), Is.EqualTo("Привет"));
        }

        [Test]
        public void FailedLoad_LeavesTheRunningLocaleFullyUsable()
        {
            var service = Create(out var source);
            Initialize(service, Ru);
            source.FailFor = En;

            var switched = service.SetLocaleAsync(En).GetAwaiter().GetResult();

            Assert.That(switched, Is.False);
            Assert.That(service.CurrentLocale, Is.EqualTo(Ru));
            Assert.That(service.GetTemplate("greeting"), Is.EqualTo("Привет"));
        }

        [Test]
        public void FailedLoad_LogsTheCause_AndKeepsTheRunningLocale()
        {
            var output = new RecordingLogOutput();
            var source = new FakeTableSource();
            var service = new LocalizationService(
                source,
                new FakeFormatter(),
                new LocalizationSettings(new[] { En, Ru }, En),
                new AppLogger(new ILogOutput[] { output }));
            Initialize(service, Ru);
            source.FailFor = En;

            var switched = service.SetLocaleAsync(En).GetAwaiter().GetResult();

            Assert.That(switched, Is.False);
            Assert.That(service.CurrentLocale, Is.EqualTo(Ru));
            Assert.That(output.Messages.Exists(message => message.Contains("table missing")), Is.True,
                "the cause of the refused switch is logged, not swallowed");
        }

        [Test]
        public void OnlyTheCurrentAndFallbackTablesAreHeld()
        {
            var service = Create(out var source);
            Initialize(service, Ru);

            Assert.That(source.LoadedLocales, Is.EquivalentTo(new[] { Ru, En }),
                "one language plus the fallback, never every language at once");
        }

        [Test]
        public void FallbackLocaleItself_NeedsNoSecondTable()
        {
            var service = Create(out var source);
            Initialize(service, En);

            Assert.That(source.LoadedLocales, Is.EqualTo(new[] { En }));
        }

        [Test]
        public void Format_AppliesArgumentsThroughTheFormatter()
        {
            var service = Create(out _);
            Initialize(service, En);

            var text = service.Format(
                "welcome",
                new LocalizationArguments(new Dictionary<string, object> { ["name"] = "Ada" }));

            Assert.That(text, Is.EqualTo("Welcome, Ada!"));
        }

        [Test]
        public void LocalizationSourceContract_StillServesTheExistingResolver()
        {
            var service = Create(out _);
            Initialize(service, Ru);
            var resolver = new LocalizationResolver(service, new FakeFormatter());

            var text = resolver.ResolveAsync("greeting").GetAwaiter().GetResult();

            Assert.That(text, Is.EqualTo("Привет"));
        }

        [Test]
        public void InitializeWithAnUnsupportedLocale_FallsBackInsteadOfFailing()
        {
            var service = Create(out _);

            service.InitializeAsync(new LocaleId("de")).GetAwaiter().GetResult();

            Assert.That(service.CurrentLocale, Is.EqualTo(En));
        }

        // A language changed twice in quick succession must end on the second choice, whichever table arrives last.
        [UnityTest]
        public IEnumerator OverlappingSwitches_EndOnTheLocaleAskedForLast() => UniTask.ToCoroutine(async () =>
        {
            var source = new GatedTableSource();
            var service = new LocalizationService(source, new FakeFormatter(), new LocalizationSettings(new[] { En, Ru }, En));
            await service.InitializeAsync(En);

            var toRussian = service.SetLocaleAsync(Ru).Preserve();
            var backToEnglish = service.SetLocaleAsync(En).Preserve();
            source.ReleaseRussian();
            await toRussian;
            await backToEnglish;

            Assert.That(service.CurrentLocale, Is.EqualTo(En));
            Assert.That(service.GetTemplate("greeting"), Is.EqualTo("Hello"));
        });

        private static void Initialize(LocalizationService service, LocaleId locale)
            => service.InitializeAsync(locale).GetAwaiter().GetResult();

        private static LocalizationService Create(out FakeTableSource source)
        {
            source = new FakeTableSource();
            return new LocalizationService(
                source,
                new FakeFormatter(),
                new LocalizationSettings(new[] { En, Ru }, En));
        }

        private sealed class GatedTableSource : ILocalizationTableSource
        {
            private readonly UniTaskCompletionSource _russian = new();

            public void ReleaseRussian() => _russian.TrySetResult();

            public async UniTask<LocalizationTable> LoadAsync(LocaleId locale, CancellationToken cancellationToken = default)
            {
                if (locale == Ru)
                    await _russian.Task;

                return new LocalizationTable(locale, new Dictionary<string, string>
                {
                    ["greeting"] = locale == Ru ? "Привет" : "Hello"
                });
            }
        }

        private sealed class FakeTableSource : ILocalizationTableSource
        {
            public List<LocaleId> LoadedLocales { get; } = new();

            public LocaleId? FailFor { get; set; }

            public UniTask<LocalizationTable> LoadAsync(
                LocaleId locale,
                CancellationToken cancellationToken = default)
            {
                if (FailFor.HasValue && FailFor.Value == locale)
                    throw new InvalidOperationException("table missing");

                LoadedLocales.Add(locale);
                var entries = locale == Ru
                    ? new Dictionary<string, string> { ["greeting"] = "Привет" }
                    : new Dictionary<string, string>
                    {
                        ["greeting"] = "Hello",
                        ["only-english"] = "English only",
                        ["welcome"] = "Welcome, {name}!"
                    };
                return UniTask.FromResult(new LocalizationTable(locale, entries));
            }
        }

        private sealed class RecordingLogOutput : ILogOutput
        {
            public List<string> Messages { get; } = new();

            public void Write(LogLevel level, string category, string message) => Messages.Add($"[{category}] {message}");
        }

        private sealed class FakeFormatter : ILocalizationFormatter
        {
            public string Format(string template, IReadOnlyDictionary<string, object> arguments)
            {
                var result = template;
                foreach (var pair in arguments)
                    result = result.Replace("{" + pair.Key + "}", pair.Value?.ToString());
                return result;
            }
        }
    }
}
