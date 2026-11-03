using Foundation.Release;
using NUnit.Framework;

namespace Foundation.Tests.Release
{
    public sealed class ClientVersionTests
    {
        [TestCase("0.9.21")]
        [TestCase("1.2")]
        [TestCase(" 2.0.1 ")]
        public void TryParse_AcceptsDottedNumbers(string text)
        {
            Assert.That(ClientVersion.TryParse(text, out _), Is.True);
        }

        [TestCase("")]
        [TestCase("1.2.3.4")]
        [TestCase("0.x.1")]
        [TestCase("-1.0.0")]
        [TestCase("1..2")]
        public void TryParse_RejectsMalformedText(string text)
        {
            Assert.That(ClientVersion.TryParse(text, out _), Is.False);
        }

        // Compared as numbers, not as text: "0.9.21" is newer than "0.9.9", and a missing part counts as zero.
        [TestCase("0.9.9", "0.9.21", true)]
        [TestCase("0.9.21", "0.10.0", true)]
        [TestCase("1.0", "1.0.0", false)]
        [TestCase("1.0.1", "1.0", false)]
        public void Comparison_IsNumeric(string lower, string higher, bool isLess)
        {
            ClientVersion.TryParse(lower, out var left);
            ClientVersion.TryParse(higher, out var right);

            Assert.That(left < right, Is.EqualTo(isLess));
        }
    }
}
