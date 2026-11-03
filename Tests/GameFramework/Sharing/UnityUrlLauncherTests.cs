using System;
using System.Threading;
using Foundation.GameFramework.Sharing;
using NUnit.Framework;

namespace Foundation.GameFramework.Tests.Sharing
{
    public sealed class UnityUrlLauncherTests
    {
        [TestCase("https://example.com/page")]
        [TestCase("mailto:support@example.com")]
        [TestCase("market://details?id=test.app")]
        public void WebMailAndStoreLinks_AreOpened(string url)
        {
            Assert.That(new UnityUrlLauncher().IsAllowed(url), Is.True);
        }

        // Content and servers hand links to the launcher; the system starts whatever app owns the scheme.
        [TestCase("file:///etc/hosts")]
        [TestCase("intent://x#Intent;scheme=app;end")]
        [TestCase("tel:+10000000")]
        [TestCase("not a url")]
        [TestCase(null)]
        public void OtherSchemes_AreRefusedUntilTheProjectAllowsThem(string url)
        {
            var launcher = new UnityUrlLauncher();

            Assert.That(launcher.IsAllowed(url), Is.False);
            Assert.Throws<ArgumentException>(() => launcher.OpenAsync(url));
        }

        [Test]
        public void AProjectCanAllowItsOwnScheme()
        {
            Assert.That(new UnityUrlLauncher(new[] { "https", "tel" }).IsAllowed("tel:+10000000"), Is.True);
        }

        [Test]
        public void OpenAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.Throws<OperationCanceledException>(
                    () => new UnityUrlLauncher().OpenAsync("https://example.com", cancellation.Token));
            }
        }
    }
}
