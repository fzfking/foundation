using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// Checks over plain HTTP that the release endpoint serves the catalog files before Addressables is asked to use
    /// them. Addressables 4.x leaves its ResourceManager throwing on every update after a failed catalog check or
    /// update ("Reentering the Update method is not allowed"), so that failure has to be avoided rather than handled.
    /// </summary>
    internal static class RemoteCatalogReadiness
    {
        private const int RequestTimeoutSeconds = 5;

        /// <summary>The release endpoint serves both the catalog hash and the catalog it describes.</summary>
        public static async UniTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            var endpoint = AddressablesEndpointRouting.CurrentEndpoint;
            if (string.IsNullOrEmpty(endpoint))
                return false;

            var hashUrl = await ReadRemoteHashUrlAsync(cancellationToken);
            if (string.IsNullOrEmpty(hashUrl))
                return false;

            // Only a hash that belongs to the selected release folder can be updated from: another host may serve an
            // unrelated build, and a catalog operation against it is exactly what hangs Addressables when it fails.
            var routed = AddressablesEndpointRouting.RouteToEndpoint(hashUrl);
            if (!routed.StartsWith(endpoint, StringComparison.Ordinal))
                return false;

            var (hashReady, catalogReady) = await UniTask.WhenAll(
                IsReachableAsync(routed, cancellationToken),
                IsReachableAsync(ToCatalogUrl(routed), cancellationToken));
            return hashReady && catalogReady;
        }

        /// <summary>Turns the catalog hash URL into the catalog URL Addressables downloads for an update.</summary>
        internal static string ToCatalogUrl(string hashUrl)
        {
            if (string.IsNullOrEmpty(hashUrl))
                return hashUrl;

            var queryStart = hashUrl.IndexOf('?');
            var path = queryStart < 0 ? hashUrl : hashUrl.Substring(0, queryStart);
            if (!path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                return hashUrl;

            var query = queryStart < 0 ? string.Empty : hashUrl.Substring(queryStart);
            return path.Substring(0, path.Length - ".hash".Length) + ".bin" + query;
        }

        /// <summary>Finds the baked remote catalog hash location in the built runtime settings.</summary>
        internal static string FindRemoteHashUrl(string runtimeSettingsJson)
        {
            if (string.IsNullOrEmpty(runtimeSettingsJson))
                return null;

            RuntimeData data;
            try
            {
                data = JsonUtility.FromJson<RuntimeData>(runtimeSettingsJson);
            }
            catch (Exception)
            {
                return null;
            }

            if (data?.m_CatalogLocations == null)
                return null;

            foreach (var location in data.m_CatalogLocations)
            {
                if (location?.m_Keys == null || string.IsNullOrEmpty(location.m_InternalId))
                    continue;

                foreach (var key in location.m_Keys)
                    if (key != null && key.EndsWith("RemoteHash", StringComparison.Ordinal))
                        return location.m_InternalId;
            }

            return null;
        }

        private static async UniTask<string> ReadRemoteHashUrlAsync(CancellationToken cancellationToken)
        {
            var settingsPath = AddressablesApi.RuntimePath + "/settings.json";

            // On desktop the runtime path is a file on disk; on Android it is a jar: URI that only UnityWebRequest reads.
            try
            {
                if (File.Exists(settingsPath))
                    return FindRemoteHashUrl(File.ReadAllText(settingsPath));
            }
            catch (Exception)
            {
                return null;
            }

            UnityWebRequest request = null;
            try
            {
                request = UnityWebRequest.Get(settingsPath);
                request.timeout = RequestTimeoutSeconds;
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                if (request.result != UnityWebRequest.Result.Success
                    || request.responseCode < 200
                    || request.responseCode >= 300)
                    return null;

                return FindRemoteHashUrl(request.downloadHandler.text);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                request?.Dispose();
            }
        }

        private static async UniTask<bool> IsReachableAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            UnityWebRequest request = null;
            try
            {
                request = UnityWebRequest.Get(url);
                request.timeout = RequestTimeoutSeconds;
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                return request.result == UnityWebRequest.Result.Success
                    && request.responseCode >= 200
                    && request.responseCode < 300;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                request?.Dispose();
            }
        }

        [Serializable]
        private sealed class RuntimeData
        {
            public CatalogLocation[] m_CatalogLocations;
        }

        [Serializable]
        private sealed class CatalogLocation
        {
            public string[] m_Keys;
            public string m_InternalId;
        }
    }
}
