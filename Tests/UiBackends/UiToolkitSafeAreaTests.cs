using Foundation.UI.Toolkit;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.UI.Backends.Tests
{
    public sealed class UiToolkitSafeAreaTests
    {
        [Test]
        public void SafeArea_MirrorsAsymmetricVerticalInsets()
        {
            var safe = SafeAreaElement.ToTopLeft(new Rect(10, 20, 1060, 1840), 1920);

            Assert.That(safe, Is.EqualTo(new Rect(10, 60, 1060, 1840)));
            Assert.That(SafeAreaElement.CalculatePadding(new Rect(0, 0, 1080, 1920), safe),
                Is.EqualTo(new Vector4(10, 60, 10, 20)));
        }

        [Test]
        public void SafeArea_OnlyPadsThePartOfTheInsetThatOverlapsTheDesign()
        {
            var bounds = new Rect(0, 0, 1080, 1920);
            // In design units, the left cutout spans the letterbox and 20 units of content.
            var safe = Rect.MinMaxRect(20, -10, 1100, 1920);

            Assert.That(SafeAreaElement.CalculatePadding(bounds, safe), Is.EqualTo(new Vector4(20, 0, 0, 0)));
        }
    }
}
