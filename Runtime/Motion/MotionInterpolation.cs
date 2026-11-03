using System;
using UnityVector2 = UnityEngine.Vector2;
using UnityVector3 = UnityEngine.Vector3;
using UnityQuaternion = UnityEngine.Quaternion;
using UnityColor = UnityEngine.Color;

namespace Foundation.Motion
{
    /// <summary>The interpolation of each value type, created once: a method group would allocate a delegate per call.</summary>
    internal static class MotionInterpolators
    {
        public static readonly Func<UnityVector2, UnityVector2, float, UnityVector2> Vector2 = static (from, to, progress) => UnityVector2.LerpUnclamped(from, to, progress);
        public static readonly Func<UnityVector3, UnityVector3, float, UnityVector3> Vector3 = static (from, to, progress) => UnityVector3.LerpUnclamped(from, to, progress);
        public static readonly Func<UnityQuaternion, UnityQuaternion, float, UnityQuaternion> Quaternion = static (from, to, progress) => UnityQuaternion.SlerpUnclamped(from, to, progress);
        public static readonly Func<UnityColor, UnityColor, float, UnityColor> Color = static (from, to, progress) => UnityColor.LerpUnclamped(from, to, progress);
    }

    /// <summary>Carries a value tween's endpoints and callback through the float tween that drives it.</summary>
    internal sealed class MotionInterpolation<TValue, TState> where TState : class
    {
        // One cached delegate per closed generic type: the float tween's callback itself allocates nothing.
        public static readonly Action<float, MotionInterpolation<TValue, TState>> Apply = static (progress, tween) => tween.Update(progress);

        private readonly TValue _from;
        private readonly TValue _to;
        private readonly TState _state;
        private readonly Action<TValue, TState> _onUpdate;
        private readonly Func<TValue, TValue, float, TValue> _interpolate;

        public MotionInterpolation(TValue from, TValue to, TState state, Action<TValue, TState> onUpdate, Func<TValue, TValue, float, TValue> interpolate)
        {
            _from = from;
            _to = to;
            _state = state;
            _onUpdate = onUpdate;
            _interpolate = interpolate;
        }

        private void Update(float progress) => _onUpdate(_interpolate(_from, _to, progress), _state);
    }
}
