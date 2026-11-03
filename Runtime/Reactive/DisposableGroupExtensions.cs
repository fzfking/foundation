using System;

namespace Foundation.Reactive
{
    public static class DisposableGroupExtensions
    {
        /// <summary>
        /// Hands <paramref name="item"/> to <paramref name="group"/> and returns it, so a subscription is owned where
        /// it is made: <c>button.Clicked.Subscribe(OnClick).AddTo(_subscriptions)</c>.
        /// </summary>
        public static T AddTo<T>(this T item, DisposableGroup group) where T : IDisposable
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            return group.Add(item);
        }
    }
}
