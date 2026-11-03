namespace Foundation.Analytics
{
    /// <summary>One analytics SDK as the consent gate sees it: started once, then switched on or off with the player's consent.</summary>
    public interface IAnalyticsSdk
    {
        /// <summary>True once the SDK has been started in this process.</summary>
        bool IsStarted { get; }

        /// <summary>Starts the SDK. Only called while it is not started.</summary>
        void Start();

        /// <summary>Turns the SDK's own sending on or off. Only called while it is started.</summary>
        void SetSendingEnabled(bool enabled);
    }
}
