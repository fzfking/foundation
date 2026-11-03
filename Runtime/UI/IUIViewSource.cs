using System;
using UnityEngine;

namespace Foundation.UI
{
    /// <summary>
    /// Where a view comes from. The router never looks inside: the <see cref="IUIViewFactory"/> of a backend builds
    /// the views from the sources it knows and rejects the rest.
    /// </summary>
    public interface IUIViewSource
    {
    }

    /// <summary>A view loaded by address through <c>IAssetProvider</c>.</summary>
    public sealed class AddressViewSource : IUIViewSource
    {
        public string Address { get; }

        public AddressViewSource(string address)
        {
            Address = string.IsNullOrWhiteSpace(address) ? throw new ArgumentException("Address is required.", nameof(address)) : address;
        }
    }

    /// <summary>A view instantiated from a prefab the definition references directly.</summary>
    public sealed class PrefabViewSource : IUIViewSource
    {
        public GameObject Prefab { get; }

        public PrefabViewSource(GameObject prefab)
        {
            Prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
        }
    }
}
