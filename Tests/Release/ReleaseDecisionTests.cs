using Foundation.Release;
using NUnit.Framework;

namespace Foundation.Tests.Release
{
    public sealed class ReleaseDecisionTests
    {
        private const string Production = "production";
        private const string StoreUrl = "market://details?id=test";

        [Test]
        public void Select_ServesTheCurrentRelease_ToClientsItIsCompatibleWith()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "0.9.21", "Prod"), Slot("r1", "0.9.0", "Prod-prev")));

            var selection = new ReleaseManager().Select(Production, Version("0.9.21"), config);

            Assert.That(selection.Kind, Is.EqualTo(ReleaseSelectionKind.Current));
            Assert.That(selection.Slot.release, Is.EqualTo("r2"));
        }

        // Clients that have not updated yet keep the release before the one they can no longer use.
        [Test]
        public void Select_ServesThePreviousRelease_ToClientsBelowTheCurrentMinimum()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "0.9.21", "Prod"), Slot("r1", "0.9.0", "Prod-prev")));

            var selection = new ReleaseManager().Select(Production, Version("0.9.5"), config);

            Assert.That(selection.Kind, Is.EqualTo(ReleaseSelectionKind.Previous));
            Assert.That(selection.Slot.path, Is.EqualTo("Prod-prev"));
        }

        [Test]
        public void Select_ReportsIncompatible_WhenNoKeptReleaseFitsTheClient()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "0.9.21", "Prod"), Slot("r1", "0.9.10", "Prod-prev")));

            var selection = new ReleaseManager().Select(Production, Version("0.9.5"), config);

            Assert.That(selection.Kind, Is.EqualTo(ReleaseSelectionKind.Incompatible));
            Assert.That(selection.CanServe, Is.False);
        }

        // A typo in a minimum must not lock players out: an unreadable version is no rule at all.
        [Test]
        public void Select_IgnoresAnUnreadableMinimum()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "soon", "Prod"), Slot("", "", "")));

            var selection = new ReleaseManager().Select(Production, Version("0.1.0"), config);

            Assert.That(selection.Kind, Is.EqualTo(ReleaseSelectionKind.Current));
        }

        [Test]
        public void Select_IsUnknown_ForAnEnvironmentTheConfigDoesNotDescribe()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")));

            var selection = new ReleaseManager().Select("rc", Version("0.9.21"), config);

            Assert.That(selection.Kind, Is.EqualTo(ReleaseSelectionKind.Unknown));
        }

        // With no current release published, the previous one is all there is: a client it cannot serve must update too.
        [Test]
        public void Select_ReportsIncompatible_WhenOnlyThePreviousReleaseIsPublishedAndItDoesNotFitTheClient()
        {
            var config = ConfigWith(Environment(Production, Slot("", "", ""), Slot("r1", "0.9.10", "Prod-prev")));

            var selection = new ReleaseManager().Select(Production, Version("0.9.5"), config);

            Assert.That(selection.Kind, Is.EqualTo(ReleaseSelectionKind.Incompatible));
        }

        [Test]
        public void Evaluate_RequiresAnUpdate_WhenTheClientIsBelowTheMinimum()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")), "0.9.21", "0.9.30");

            var status = Evaluate(config, "0.9.20");

            Assert.That(status, Is.EqualTo(ClientUpdateStatus.Required));
        }

        [Test]
        public void Evaluate_RecommendsAnUpdate_BetweenTheMinimumAndTheRecommendedVersion()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")), "0.9.21", "0.9.30");

            Assert.That(Evaluate(config, "0.9.25"), Is.EqualTo(ClientUpdateStatus.Recommended));
            Assert.That(Evaluate(config, "0.9.30"), Is.EqualTo(ClientUpdateStatus.None));
        }

        // Content the client cannot play requires an update even when the policy itself asks for nothing.
        [Test]
        public void Evaluate_RequiresAnUpdate_WhenTheCurrentReleaseIsIncompatible()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "0.9.40", "Prod"), Slot("", "", "")));
            var selection = new ReleaseManager().Select(Production, Version("0.9.21"), config);

            var status = new ClientUpdateService(StoreUrl).Evaluate(Version("0.9.21"), config, selection);

            Assert.That(status, Is.EqualTo(ClientUpdateStatus.Required));
        }

        [Test]
        public void Evaluate_NeverBlocks_OnUnreadablePolicyValues()
        {
            var config = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")), "next", "soon");

            Assert.That(Evaluate(config, "0.0.1"), Is.EqualTo(ClientUpdateStatus.None));
        }

        [Test]
        public void StoreUrl_PrefersTheServerValue_AndFallsBackToTheBuild()
        {
            var service = new ClientUpdateService(StoreUrl);
            var configured = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")));
            configured.client.storeUrl = "https://store.example/app";

            Assert.That(service.StoreUrl(configured), Is.EqualTo("https://store.example/app"));
            Assert.That(service.StoreUrl(ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")))), Is.EqualTo(StoreUrl));
        }

        // The update button opens the server's link; a config that names anything but a store link must not reach it.
        [TestCase("http://store.example/app")]
        [TestCase("file:///data/data/app/secret")]
        [TestCase("intent://scan/#Intent;scheme=zxing;end")]
        [TestCase("javascript:alert(1)")]
        [TestCase("not a url")]
        public void StoreUrl_IgnoresAServerValueThatIsNotAStoreLink(string serverValue)
        {
            var service = new ClientUpdateService(StoreUrl);
            var configured = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")));
            configured.client.storeUrl = serverValue;

            Assert.That(service.StoreUrl(configured), Is.EqualTo(StoreUrl));
        }

        [TestCase("market://details?id=test.app")]
        [TestCase("itms-apps://apps.apple.com/app/id1")]
        public void StoreUrl_KeepsTheNativeStoreSchemes(string serverValue)
        {
            var service = new ClientUpdateService("https://fallback.example/");
            var configured = ConfigWith(Environment(Production, Slot("r2", "", "Prod"), Slot("", "", "")));
            configured.client.storeUrl = serverValue;

            Assert.That(service.StoreUrl(configured), Is.EqualTo(serverValue));
        }

        [TestCase("https://cdn.example/game", "Prod", "https://cdn.example/game/Prod/")]
        [TestCase("https://cdn.example/game/", "/Prod/", "https://cdn.example/game/Prod/")]
        public void Resolve_BuildsTheFolderOfTheSelectedRelease(string root, string path, string expected)
        {
            var selection = new ReleaseSelection(ReleaseSelectionKind.Current, Slot("r2", "", path));

            Assert.That(new ContentEndpointResolver(root).Resolve(selection), Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_ReturnsNothing_ForAReleaseThatCannotBeServed()
        {
            var resolver = new ContentEndpointResolver("https://cdn.example/game");

            Assert.That(resolver.Resolve(new ReleaseSelection(ReleaseSelectionKind.Incompatible, Slot("r2", "", "Prod"))), Is.Null);
            Assert.That(resolver.Resolve(new ReleaseSelection(ReleaseSelectionKind.Current, Slot("r2", "", ""))), Is.Null);
        }

        [TestCase("..")]
        [TestCase("../other")]
        [TestCase("Prod/../other")]
        [TestCase("%2e%2e")]
        [TestCase("folder\\other")]
        [TestCase("folder//other")]
        [TestCase(".. /other")]
        public void Resolve_ReturnsNothing_ForAPathOutsideTheReleaseFolder(string path)
        {
            var selection = new ReleaseSelection(ReleaseSelectionKind.Current, Slot("r2", "", path));

            Assert.That(new ContentEndpointResolver("https://cdn.example/game").Resolve(selection), Is.Null);
        }

        [Test]
        public void Resolve_EscapesEachSafeFolderSegment()
        {
            var selection = new ReleaseSelection(ReleaseSelectionKind.Current, Slot("r2", "", "Release 1/final#2"));

            Assert.That(
                new ContentEndpointResolver("https://cdn.example/game").Resolve(selection),
                Is.EqualTo("https://cdn.example/game/Release%201/final%232/"));
        }

        private static ClientUpdateStatus Evaluate(ReleaseConfig config, string clientVersion)
        {
            var version = Version(clientVersion);
            var selection = new ReleaseManager().Select(Production, version, config);
            return new ClientUpdateService(StoreUrl).Evaluate(version, config, selection);
        }

        private static ClientVersion Version(string text)
        {
            ClientVersion.TryParse(text, out var version);
            return version;
        }

        private static ReleaseSlot Slot(string release, string minClient, string path)
            => new ReleaseSlot { release = release, minClient = minClient, path = path };

        private static ReleaseEnvironment Environment(string id, ReleaseSlot current, ReleaseSlot previous)
            => new ReleaseEnvironment { id = id, current = current, previous = previous };

        private static ReleaseConfig ConfigWith(ReleaseEnvironment environment, string minimum = "", string recommended = "")
            => new ReleaseConfig
            {
                schema = ReleaseConfig.SupportedSchema,
                client = new ClientPolicy { minimum = minimum, recommended = recommended, storeUrl = "" },
                environments = new[] { environment },
            };
    }
}
