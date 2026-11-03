using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Foundation.Content
{
    internal sealed class LoadedAssetRegistry
    {
        private readonly object _lock = new();
        private readonly Dictionary<Key, Entry> _loaded = new();
        private readonly Dictionary<Key, UniTaskCompletionSource> _pending = new();
        // References reserved by callers waiting on a pending load; they become the entry's references on completion.
        private readonly Dictionary<Key, int> _reservations = new();

        public int LoadedCount
        {
            get
            {
                lock (_lock)
                    return _loaded.Count;
            }
        }

        public int PendingCount
        {
            get
            {
                lock (_lock)
                    return _pending.Count;
            }
        }

        public bool TryGetLoaded(Key key, out Entry entry)
        {
            lock (_lock)
                return _loaded.TryGetValue(key, out entry);
        }

        public int AddRef(Key key)
        {
            lock (_lock)
            {
                if (!_loaded.TryGetValue(key, out var entry))
                    return -1;

                entry.RefCount++;
                return entry.RefCount;
            }
        }

        public int ReleaseRef(Key key)
        {
            lock (_lock)
            {
                if (!_loaded.TryGetValue(key, out var entry))
                    return -1;

                entry.RefCount--;

                if (entry.RefCount <= 0)
                {
                    _loaded.Remove(key);
                    return 0;
                }

                return entry.RefCount;
            }
        }

        public int GetRefCount(Key key)
        {
            lock (_lock)
            {
                if (!_loaded.TryGetValue(key, out var entry))
                    return 0;

                return entry.RefCount;
            }
        }

        /// <summary>
        /// Joins the load in progress for <paramref name="key"/> or starts one. The caller reserves a
        /// reference either way, so the asset stays loaded while anyone still waits for it.
        /// </summary>
        public UniTaskCompletionSource JoinPending(Key key, out bool created)
        {
            lock (_lock)
            {
                _reservations.TryGetValue(key, out var reserved);
                _reservations[key] = reserved + 1;

                if (_pending.TryGetValue(key, out var existing))
                {
                    created = false;
                    return existing;
                }

                var completion = new UniTaskCompletionSource();
                _pending[key] = completion;
                created = true;
                return completion;
            }
        }

        /// <summary>
        /// Completes the load that produced <paramref name="entry"/>: the references reserved meanwhile
        /// become the entry's. Returns false and registers nothing when every caller left or the load was
        /// abandoned; the caller then releases the asset.
        /// </summary>
        public bool CompletePending(Key key, UniTaskCompletionSource completion, Entry entry)
        {
            lock (_lock)
            {
                if (!_pending.TryGetValue(key, out var current) || !ReferenceEquals(current, completion))
                    return false;

                _pending.Remove(key);
                _reservations.TryGetValue(key, out var reserved);
                _reservations.Remove(key);

                entry.RefCount = reserved;
                if (reserved <= 0)
                    return false;

                _loaded[key] = entry;
                return true;
            }
        }

        /// <summary>Ends a load that failed. Its reservations end with it; the callers waiting on it see the failure.</summary>
        public void FailPending(Key key, UniTaskCompletionSource completion)
        {
            lock (_lock)
            {
                if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, completion))
                {
                    _pending.Remove(key);
                    _reservations.Remove(key);
                }
            }
        }

        /// <summary>
        /// Gives back the reference a caller reserved when it stopped waiting before it got the asset.
        /// Returns the entry to release if that was the last reference, otherwise null.
        /// </summary>
        public Entry AbandonReservation(Key key, UniTaskCompletionSource completion)
        {
            lock (_lock)
            {
                if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, completion))
                {
                    _reservations.TryGetValue(key, out var reserved);
                    _reservations[key] = reserved - 1;
                    return null;
                }

                // The load has completed, so this caller's reservation is one of the entry's references.
                if (completion.Task.Status != UniTaskStatus.Succeeded || !_loaded.TryGetValue(key, out var entry))
                    return null;

                entry.RefCount--;
                if (entry.RefCount > 0)
                    return null;

                _loaded.Remove(key);
                return entry;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _loaded.Clear();

                foreach (var kvp in _pending)
                {
                    if (!kvp.Value.Task.Status.IsCompleted())
                        kvp.Value.TrySetCanceled();
                }

                _pending.Clear();
                _reservations.Clear();
            }
        }

        public IReadOnlyList<Entry> GetAllEntries()
        {
            lock (_lock)
            {
                var list = new List<Entry>(_loaded.Values);
                return list;
            }
        }

        // One load per address and asset type. The same address asked for as another type is another asset here: a
        // sub-asset such as a Sprite in a Texture2D sheet loads on its own, instead of failing on the other type's entry.
        public readonly struct Key : IEquatable<Key>
        {
            public string Address { get; }

            public Type AssetType { get; }

            public Key(string address, Type assetType)
            {
                Address = address ?? throw new ArgumentNullException(nameof(address));
                AssetType = assetType ?? throw new ArgumentNullException(nameof(assetType));
            }

            public bool Equals(Key other) => Address == other.Address && AssetType == other.AssetType;

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Address, AssetType);
        }

        public sealed class Entry
        {
            public object Asset;
            public object Handle;
            public int RefCount;
        }
    }
}
