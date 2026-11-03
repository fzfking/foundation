namespace Foundation.Content
{
    /// <summary>
    /// How the content phase of the boot treats the network. A project chooses one from its own settings and passes it to
    /// the content boot step of its delivery backend.
    /// </summary>
    public enum ContentStartupStrategy
    {
        /// <summary>
        /// Starts on the content already on the device and updates it when the host answers. The unit is the whole content of
        /// the active catalog or package version: the boot downloads all of it, waits for a connection while a device has
        /// nothing usable, and never plays on a half-updated set.
        /// </summary>
        OfflineFirst,

        /// <summary>
        /// The backend's own behavior with no startup synchronization: it is initialized as its settings dictate, nothing is
        /// downloaded ahead of time, and a failure is an error rather than a wait. Content that is not on the device is
        /// fetched when it is first loaded.
        /// </summary>
        Standard,
    }
}
