using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// A serialized reference to a UnityEngine.Object that implements <typeparamref name="T"/> (a
    /// ScriptableObject or a scene component), so an inspector list can hold strategies without a concrete
    /// base class. The drawer accepts only objects, or components of a dropped GameObject, that implement the
    /// interface.
    /// </summary>
    [Serializable]
    public struct InterfaceReference<T> where T : class
    {
        [SerializeField] private Object target;

        /// <summary>The referenced object as <typeparamref name="T"/>; null when nothing (or something unusable) is assigned.</summary>
        public T Value => target as T;

        public InterfaceReference(T value)
        {
            target = value as Object;
            if (value != null && target == null)
                throw new ArgumentException($"{value.GetType().Name} is not a UnityEngine.Object.", nameof(value));
        }

        public bool TryGetValue(out T value)
        {
            value = target as T;
            return value != null;
        }
    }
}
