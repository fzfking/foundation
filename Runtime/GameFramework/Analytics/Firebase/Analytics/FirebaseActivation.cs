using Cysharp.Threading.Tasks;
using Foundation.GameFramework.Firebase;

namespace Foundation.GameFramework.Analytics.Firebase
{
    /// <summary>Kept in this assembly for source compatibility; the shared implementation is <see cref="FirebaseActivationCore"/>.</summary>
    public static class FirebaseActivation
    {
        public static UniTask<bool> EnsureReadyAsync() => FirebaseActivationCore.EnsureReadyAsync();
    }
}
