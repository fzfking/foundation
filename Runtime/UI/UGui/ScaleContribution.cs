using UnityEngine;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// One effect's share of a transform's scale: the scale is its own size times the factor of every contribution
    /// on it. A contribution changes the scale by the ratio between its new and old factor and removes itself by
    /// dividing out its current one, so a press punch, a selected tab and a pulse that overlap in any order end
    /// where they started (to float precision), and nothing has to remember a "resting scale" or live on the transform.
    /// </summary>
    /// <remarks>
    /// Assumes nobody assigns <see cref="Transform.localScale"/> outright while a contribution is applied; an effect
    /// that changes the size should itself be a contribution. A factor component must not be zero (it could not be
    /// divided out again); a zero is raised to <see cref="MinimumFactor"/>.
    /// </remarks>
    public sealed class ScaleContribution
    {
        public const float MinimumFactor = 0.01f;

        private readonly Transform _target;
        private Vector3 _factor = Vector3.one;

        public ScaleContribution(Transform target)
        {
            _target = target;
        }

        public Vector3 Factor => _factor;

        public void Set(float uniformFactor) => Set(new Vector3(uniformFactor, uniformFactor, uniformFactor));

        public void Set(Vector3 factor)
        {
            factor = new Vector3(Clamp(factor.x), Clamp(factor.y), Clamp(factor.z));
            if (_target != null)
            {
                var scale = _target.localScale;
                _target.localScale = new Vector3(
                    scale.x / _factor.x * factor.x,
                    scale.y / _factor.y * factor.y,
                    scale.z / _factor.z * factor.z);
            }

            _factor = factor;
        }

        /// <summary>Takes this contribution off the scale.</summary>
        public void Release() => Set(Vector3.one);

        private static float Clamp(float value) => Mathf.Abs(value) < MinimumFactor ? MinimumFactor : value;
    }
}
