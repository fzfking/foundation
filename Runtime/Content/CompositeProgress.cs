using System;
using System.Collections.Generic;

namespace Foundation.Content
{
    public sealed class CompositeProgress
    {
        private readonly IProgress<float> _target;
        private readonly float[] _weights;
        private readonly float[] _values;
        private readonly float _totalWeight;

        public CompositeProgress(IProgress<float> target, IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0)
                throw new ArgumentException("At least one weight is required.", nameof(weights));

            _target = target;
            _weights = new float[weights.Count];
            _values = new float[weights.Count];

            var total = 0f;
            for (var index = 0; index < weights.Count; index++)
            {
                if (!float.IsFinite(weights[index]) || weights[index] < 0f)
                    throw new ArgumentOutOfRangeException(nameof(weights), "Weights must be finite and non-negative.");

                _weights[index] = weights[index];
                total += weights[index];
                if (!float.IsFinite(total))
                    throw new ArgumentOutOfRangeException(nameof(weights), "The sum of weights must be finite.");
            }

            _totalWeight = total;
        }

        public IProgress<float> Slot(int index)
        {
            if (index < 0 || index >= _weights.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return new SlotProgress(this, index);
        }

        private void Report(int index, float value)
        {
            if (!float.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Progress must be finite.");

            _values[index] = value < 0f ? 0f : value > 1f ? 1f : value;

            if (_totalWeight <= 0f)
            {
                _target?.Report(0f);
                return;
            }

            var weighted = 0f;
            for (var i = 0; i < _values.Length; i++)
                weighted += _values[i] * _weights[i];

            _target?.Report(weighted / _totalWeight);
        }

        private sealed class SlotProgress : IProgress<float>
        {
            private readonly CompositeProgress _owner;
            private readonly int _index;

            public SlotProgress(CompositeProgress owner, int index)
            {
                _owner = owner;
                _index = index;
            }

            public void Report(float value) => _owner.Report(_index, value);
        }
    }
}
