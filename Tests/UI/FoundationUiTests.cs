using Foundation.UI.UGui;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Foundation.UI;
using Foundation.Localization;
using Foundation.Reactive;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using Foundation.Motion;

namespace Foundation.Tests.UI
{
    public sealed class FoundationUiTests
    {
        [Test]
        public void ATweeningStrategy_WithoutAMotionService_FailsInsteadOfAnimatingOnADefault()
        {
            var gameObject = new GameObject("button");
            var button = gameObject.AddComponent<FoundationButton>();
            using var context = new FoundationButtonStrategyContext(button);
            var motion = new PlayerLoopMotionService();

            Assert.Throws<InvalidOperationException>(() => context.RequireMotion());
            context.SetMotion(motion);
            Assert.That(context.RequireMotion(), Is.SameAs(motion));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void FoundationButton_ForwardsClickToUnityAndStreamSubscribers()
        {
            var gameObject = new GameObject("button");
            var button = gameObject.AddComponent<FoundationButton>();
            var unityClicks = 0;
            var streamClicks = 0;
            button.onClick.AddListener(() => unityClicks++);
            using var subscription = button.Clicked.Subscribe(_ => streamClicks++);

            button.OnPointerClick(new PointerEventData(null)
            {
                button = PointerEventData.InputButton.Left,
            });

            Assert.That(unityClicks, Is.EqualTo(1));
            Assert.That(streamClicks, Is.EqualTo(1));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void FoundationButton_AsyncSubscriptionBlocksReentrantClick()
        {
            var gameObject = new GameObject("button");
            var button = gameObject.AddComponent<FoundationButton>();
            var unityClicks = 0;
            var completion = new UniTaskCompletionSource();
            button.onClick.AddListener(() => unityClicks++);
            using var subscription = button.SubscribeAsync((_, _) => completion.Task);

            button.OnPointerClick(new PointerEventData(null)
            {
                button = PointerEventData.InputButton.Left,
            });
            button.OnPointerClick(new PointerEventData(null)
            {
                button = PointerEventData.InputButton.Left,
            });

            Assert.That(unityClicks, Is.EqualTo(1));
            Assert.That(button.Busy.Value, Is.True);

            completion.TrySetResult();
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void FoundationButton_WaitsForEveryHandlerAfterOneIsCancelled()
        {
            var gameObject = new GameObject("button");
            var button = gameObject.AddComponent<FoundationButton>();
            var cancelled = new UniTaskCompletionSource();
            var pending = new UniTaskCompletionSource();
            using var first = button.SubscribeAsync((_, _) => cancelled.Task);
            using var second = button.SubscribeAsync((_, _) => pending.Task);

            button.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
            cancelled.TrySetCanceled();

            Assert.That(button.Busy.Value, Is.True);

            pending.TrySetResult();

            Assert.That(button.Busy.Value, Is.False);
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void UITabGroup_WaitsForOtherHandlersAndRestoresThePreviousSelectionAfterFailure()
        {
            var groupObject = new GameObject("tabs");
            groupObject.SetActive(false);
            var firstObject = new GameObject("first-tab");
            firstObject.transform.SetParent(groupObject.transform);
            var secondObject = new GameObject("second-tab");
            secondObject.transform.SetParent(groupObject.transform);
            var firstTab = firstObject.AddComponent<UITabItem>();
            var secondTab = secondObject.AddComponent<UITabItem>();
            SetField(firstTab, "tabId", "first");
            SetField(secondTab, "tabId", "second");
            var group = groupObject.AddComponent<UITabGroup>();
            SetField(group, "items", new[] { firstTab, secondTab });
            try
            {
                // EditMode does not run this component's lifetime callbacks.
                typeof(UITabGroup).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(group, null);
                groupObject.SetActive(true);
                group.SelectAsync("first").GetAwaiter().GetResult();
                var pending = new UniTaskCompletionSource();
                group.AddSwitchHandler(new ThrowingTabHandler());
                group.AddSwitchHandler(new PendingTabHandler(pending));

                var selection = group.SelectAsync("second");
                Assert.That(group.IsTransitioning, Is.True);
                pending.TrySetResult();

                Assert.Throws<InvalidOperationException>(() => selection.GetAwaiter().GetResult());
                Assert.That(group.SelectedTabId, Is.EqualTo("first"));
                Assert.That(group.IsTransitioning, Is.False);
            }
            finally
            {
                typeof(UITabGroup).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(group, null);
                Object.DestroyImmediate(groupObject);
            }
        }

        [Test]
        public void UITabGroup_TrySelectAsyncReportsBusyWhileATransitionRuns()
        {
            var group = CreateTabGroup(out var groupObject, "first", "second");
            try
            {
                group.SelectAsync("first").GetAwaiter().GetResult();
                var pending = new UniTaskCompletionSource();
                group.AddSwitchHandler(new PendingTabHandler(pending));

                var transition = group.SelectAsync("second");
                Assert.That(group.IsTransitioning, Is.True);
                Assert.That(group.TrySelectAsync("first").GetAwaiter().GetResult(), Is.False);
                // SelectAsync keeps its old silent behaviour for the same busy state.
                group.SelectAsync("first").GetAwaiter().GetResult();
                Assert.That(group.IsTransitioning, Is.True);

                pending.TrySetResult();
                transition.GetAwaiter().GetResult();
                Assert.That(group.SelectedTabId, Is.EqualTo("second"));
            }
            finally
            {
                DestroyTabGroup(groupObject, group);
            }
        }

        [Test]
        public void UITabGroup_BusySelectionIsNotQueuedForLater()
        {
            var group = CreateTabGroup(out var groupObject, "first", "second");
            try
            {
                group.SelectAsync("first").GetAwaiter().GetResult();
                var pending = new UniTaskCompletionSource();
                var changes = new List<string>();
                group.AddSwitchHandler(new PendingTabHandler(pending));
                group.SelectedTabChanged += changes.Add;

                var transition = group.SelectAsync("second");
                Assert.That(group.TrySelectAsync("first").GetAwaiter().GetResult(), Is.False);
                pending.TrySetResult();
                transition.GetAwaiter().GetResult();

                Assert.That(group.SelectedTabId, Is.EqualTo("second"));
                Assert.That(changes, Is.EqualTo(new[] { "second" }));
                Assert.That(group.TrySelectAsync("first").GetAwaiter().GetResult(), Is.True);
                Assert.That(group.SelectedTabId, Is.EqualTo("first"));
                Assert.That(changes, Is.EqualTo(new[] { "second", "first" }));
            }
            finally
            {
                DestroyTabGroup(groupObject, group);
            }
        }

        [Test]
        public void UITabGroup_TrySelectAsyncOnTheSelectedTabSucceedsWithoutATransition()
        {
            var group = CreateTabGroup(out var groupObject, "first", "second");
            try
            {
                group.SelectAsync("first").GetAwaiter().GetResult();
                ITabGroupTrySelect selectable = group;
                var changed = 0;
                var settled = 0;
                group.SelectedTabChanged += _ => changed++;
                group.SelectedTabSettled += _ => settled++;

                Assert.That(selectable.TrySelectAsync("first").GetAwaiter().GetResult(), Is.True);

                Assert.That(changed, Is.Zero);
                Assert.That(settled, Is.Zero);
                Assert.That(group.IsTransitioning, Is.False);
                Assert.That(group.SelectedTabId, Is.EqualTo("first"));
            }
            finally
            {
                DestroyTabGroup(groupObject, group);
            }
        }

        [Test]
        public void UITabGroup_SettledFailureKeepsTheCommittedSelectionAndHandlerContent()
        {
            var group = CreateTabGroup(out var groupObject, "first", "second");
            try
            {
                group.SelectAsync("first").GetAwaiter().GetResult();
                var handler = new RecordingTabHandler();
                group.AddSwitchHandler(handler);
                group.SelectedTabSettled += ThrowFromSettled;

                Assert.Throws<InvalidOperationException>(
                    () => group.SelectAsync("second").GetAwaiter().GetResult());

                Assert.That(group.SelectedTabId, Is.EqualTo("second"));
                Assert.That(handler.Calls, Is.EqualTo(1));
                Assert.That(handler.Content, Is.EqualTo("second"));
                Assert.That(group.IsTransitioning, Is.False);
                group.TryGetItem("first", out var firstTab);
                Assert.That(firstTab.IsSelected, Is.False);

                group.SelectedTabSettled -= ThrowFromSettled;
                Assert.That(group.TrySelectAsync("first").GetAwaiter().GetResult(), Is.True);
                Assert.That(group.SelectedTabId, Is.EqualTo("first"));
                Assert.That(handler.Content, Is.EqualTo("first"));
            }
            finally
            {
                DestroyTabGroup(groupObject, group);
            }
        }

        [Test]
        public void UITabGroup_SelectionFromSettledIsRejectedInsteadOfBeingLost()
        {
            var group = CreateTabGroup(out var groupObject, "first", "second", "third");
            try
            {
                group.SelectAsync("first").GetAwaiter().GetResult();
                var changes = new List<string>();
                bool? reentrantResult = null;
                group.SelectedTabChanged += changes.Add;
                group.SelectedTabSettled += _ =>
                {
                    reentrantResult = group.TrySelectAsync("third").GetAwaiter().GetResult();
                    group.SelectAsync("third").GetAwaiter().GetResult();
                };

                Assert.That(group.TrySelectAsync("second").GetAwaiter().GetResult(), Is.True);

                Assert.That(reentrantResult, Is.False);
                Assert.That(group.SelectedTabId, Is.EqualTo("second"));
                Assert.That(changes, Is.EqualTo(new[] { "second" }));
            }
            finally
            {
                DestroyTabGroup(groupObject, group);
            }
        }

        [Test]
        public void UITabGroup_TrySelectAsyncCompletesTheTransitionAndReportsIt()
        {
            var group = CreateTabGroup(out var groupObject, "first", "second");
            try
            {
                group.SelectAsync("first").GetAwaiter().GetResult();
                var handler = new RecordingTabHandler();
                var events = new List<string>();
                group.AddSwitchHandler(handler);
                group.SelectedTabChanged += _ => events.Add("changed");
                group.SelectedTabSettled += _ => events.Add("settled");

                Assert.That(group.TrySelectAsync("second").GetAwaiter().GetResult(), Is.True);

                Assert.That(group.SelectedTabId, Is.EqualTo("second"));
                Assert.That(handler.Content, Is.EqualTo("second"));
                Assert.That(events, Is.EqualTo(new[] { "changed", "settled" }));
                Assert.That(group.IsTransitioning, Is.False);
            }
            finally
            {
                DestroyTabGroup(groupObject, group);
            }
        }

        [Test]
        public void LocalizedText_ChangesKeyAndFormatsArgumentsForAnyTextTarget()
        {
            var gameObject = new GameObject("localized-text");
            var localizedText = gameObject.AddComponent<LocalizedText>();
            var target = new RecordingTextTarget();
            var localization = new FakeLocalizationService();
            localizedText.SetTarget(target);
            localizedText.Bind(localization);

            localizedText.SetKey("welcome");
            localizedText.SetArgument("name", "Ada");
            Assert.That(target.Value, Is.EqualTo("Welcome, Ada!"));

            localizedText.SetKey("goodbye");
            Assert.That(target.Value, Is.EqualTo("Goodbye"));
            Object.DestroyImmediate(gameObject);
        }

        private sealed class RecordingTextTarget : ILocalizedTextTarget
        {
            public string Value { get; private set; }

            public void SetText(string value) => Value = value;
        }

        private static void SetField<T>(T target, string fieldName, object value)
            => typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static UITabGroup CreateTabGroup(out GameObject groupObject, params string[] tabIds)
        {
            groupObject = new GameObject("tabs");
            groupObject.SetActive(false);
            var tabs = new UITabItem[tabIds.Length];
            for (var index = 0; index < tabIds.Length; index++)
            {
                var tabObject = new GameObject(tabIds[index] + "-tab");
                tabObject.transform.SetParent(groupObject.transform);
                var tab = tabObject.AddComponent<UITabItem>();
                SetField(tab, "tabId", tabIds[index]);
                tabs[index] = tab;
            }

            var group = groupObject.AddComponent<UITabGroup>();
            SetField(group, "items", tabs);
            // EditMode does not run this component's lifetime callbacks.
            typeof(UITabGroup).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(group, null);
            groupObject.SetActive(true);
            return group;
        }

        private static void DestroyTabGroup(GameObject groupObject, UITabGroup group)
        {
            typeof(UITabGroup).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(group, null);
            Object.DestroyImmediate(groupObject);
        }

        private static void ThrowFromSettled(string _)
            => throw new InvalidOperationException("settled subscriber failed");

        private sealed class RecordingTabHandler : ITabSwitchHandler
        {
            public string Content { get; private set; }
            public int Calls { get; private set; }

            public UniTask HandleAsync(TabSwitch tabSwitch, CancellationToken cancellationToken)
            {
                Calls++;
                Content = tabSwitch.TabId;
                return UniTask.CompletedTask;
            }
        }

        private sealed class ThrowingTabHandler : ITabSwitchHandler
        {
            public UniTask HandleAsync(TabSwitch tabSwitch, CancellationToken cancellationToken)
                => throw new InvalidOperationException("tab handler failed");
        }

        private sealed class PendingTabHandler : ITabSwitchHandler
        {
            private readonly UniTaskCompletionSource _completion;
            public PendingTabHandler(UniTaskCompletionSource completion) => _completion = completion;
            public UniTask HandleAsync(TabSwitch tabSwitch, CancellationToken cancellationToken) => _completion.Task;
        }

        private sealed class FakeLocalizationService : ILocalizationService
        {
            private readonly ReactiveValue<LocaleId> _locale = new(new LocaleId("en"));

            public LocaleId CurrentLocale => _locale.Value;
            public IReadOnlyReactiveValue<LocaleId> Locale => _locale;
            public LocalizationSettings Settings { get; } = new(
                new[] { new LocaleId("en") },
                new LocaleId("en"));

            public UniTask InitializeAsync(LocaleId locale, CancellationToken cancellationToken = default)
                => UniTask.CompletedTask;

            public UniTask<bool> SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken = default)
                => UniTask.FromResult(false);

            public string GetTemplate(LocalizationKey key) => key.Value switch
            {
                "welcome" => "Welcome, {name}!",
                "goodbye" => "Goodbye",
                _ => "[MISSING:" + key.Value + "]",
            };

            public string Format(LocalizationKey key, LocalizationArguments arguments)
            {
                var value = GetTemplate(key);
                foreach (var pair in arguments.Values)
                    value = value.Replace("{" + pair.Key + "}", pair.Value?.ToString());
                return value;
            }
        }
    }
}
