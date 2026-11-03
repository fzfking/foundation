using System.Threading;
using System;
using Foundation.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.UI
{
    public sealed class UIDefinitionRegistryTests
    {
        private sealed class TestView : IUIView
        {
            public UniTask ShowAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask HideAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        [Test]
        public void DuplicateRegistration_FailsClearly()
        {
            var registry = new UIDefinitionRegistry();
            registry.Register<TestView>(new UiDefinition("test", new AddressViewSource("ui/test"), UILayer.Popup, UICachePolicy.KeepAlive, true, true));
            Assert.Throws<InvalidOperationException>(() => registry.Register<TestView>(new UiDefinition("other", new AddressViewSource("ui/other"), UILayer.Popup, UICachePolicy.KeepAlive, true, true)));
        }

        [Test]
        public void MissingDefinition_FailsClearly()
        {
            Assert.Throws<InvalidOperationException>(() => new UIDefinitionRegistry().Resolve<TestView>());
        }
    }
}
