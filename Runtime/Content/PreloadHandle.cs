using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace Foundation.Content
{
    public interface IPreloadHandle : IDisposable
    {
    }

    internal sealed class PreloadHandle : IPreloadHandle
    {
        private List<IAssetHandle<Object>> _handles;

        public PreloadHandle(List<IAssetHandle<Object>> handles)
        {
            _handles = handles;
        }

        public void Dispose()
        {
            if (_handles == null)
                return;

            foreach (var handle in _handles)
                handle.Dispose();

            _handles = null;
        }
    }
}
