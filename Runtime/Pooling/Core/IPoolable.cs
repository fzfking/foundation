namespace Foundation.Pooling
{
    /// <summary>
    /// Lifecycle of a pooled instance. <see cref="OnReturned"/> must undo everything <see cref="OnRented"/>
    /// and the user did (subscriptions, tweens, bound data, pending async work): the pool never resets
    /// component state itself.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>Once, right after the instance was created and before its first use.</summary>
        void OnCreated();
        void OnRented();
        void OnReturned();
        /// <summary>Once, before the instance is destroyed.</summary>
        void OnDestroying();
    }
}
