namespace Foundation.Pooling
{
    /// <summary>
    /// How a pool creates, prepares and destroys one kind of instance. The pool owns bookkeeping (counts,
    /// limits, safety); the strategy owns the instance, so prefabs, plain objects and audio sources share one
    /// pool.
    /// </summary>
    public interface IPoolStrategy<T>
        where T : class
    {
        T Create();
        
        /// <summary>False when the instance was destroyed behind the pool's back (for example a scene unload).</summary>
        bool IsValid(T item);
        
        /// <summary>Makes an idle instance ready for use. It must not rely on anything left from a previous use.</summary>
        void OnRent(T item);

        /// <summary>Puts an instance back into its idle state and releases whatever the user bound to it.</summary>
        void OnReturn(T item);

        /// <summary>
        /// Destroys an instance for good. Also called for invalid instances to release bookkeeping, so it
        /// must tolerate an already destroyed object.
        /// </summary>
        void OnDestroy(T item);
    }
}
