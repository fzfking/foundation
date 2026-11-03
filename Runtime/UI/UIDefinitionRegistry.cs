using System;
using System.Collections.Generic;

namespace Foundation.UI
{
    public interface IUIDefinitionRegistry
    {
        void Register<TView>(UiDefinition definition) where TView : class, IUIView;
        UiDefinition Resolve<TView>() where TView : class, IUIView;
        UiDefinition Resolve(Type viewType);
    }

    public sealed class UIDefinitionRegistry : IUIDefinitionRegistry
    {
        private readonly Dictionary<Type, UiDefinition> _definitions = new();

        public void Register<TView>(UiDefinition definition) where TView : class, IUIView
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (!_definitions.TryAdd(typeof(TView), definition))
                throw new InvalidOperationException($"UI definition for '{typeof(TView).FullName}' is already registered.");
        }

        public UiDefinition Resolve<TView>() where TView : class, IUIView => Resolve(typeof(TView));

        public UiDefinition Resolve(Type viewType)
        {
            if (!_definitions.TryGetValue(viewType, out var definition))
                throw new InvalidOperationException($"No UI definition is registered for '{viewType.FullName}'.");
            return definition;
        }
    }
}
