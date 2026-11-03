using System;

namespace Foundation.UI
{
    public sealed class UiDefinition
    {
        public string ViewId { get; }

        /// <summary>Where the view is created from; only the backend's factory interprets it.</summary>
        public IUIViewSource Source { get; }

        public UILayer Layer { get; }
        public UICachePolicy CachePolicy { get; }
        public bool BlockInput { get; }

        /// <summary>
        /// Whether Back may close the view. A view implementing <see cref="IUIBackHandler"/> decides itself
        /// and this flag is ignored.
        /// </summary>
        public bool AllowBack { get; }

        public bool AllowDuplicates { get; }

        public UiDefinition(
            string viewId,
            IUIViewSource source,
            UILayer layer,
            UICachePolicy cachePolicy,
            bool blockInput,
            bool allowBack,
            bool allowDuplicates = false)
        {
            if (allowDuplicates && cachePolicy == UICachePolicy.KeepAlive)
                throw new ArgumentException("Duplicate views require DestroyOnClose caching.", nameof(allowDuplicates));

            ViewId = string.IsNullOrWhiteSpace(viewId) ? throw new ArgumentException("ViewId is required.", nameof(viewId)) : viewId;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Layer = layer;
            CachePolicy = cachePolicy;
            BlockInput = blockInput;
            AllowBack = allowBack;
            AllowDuplicates = allowDuplicates;
        }
    }
}
