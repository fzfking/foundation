using System;
using System.Collections.Generic;

namespace Foundation.Spawning
{
    // A request makes a copy and returns true, or is skipped (cancelled, parent gone) and returns false; skipped
    // requests use no budget.
    internal sealed class UiCopyQueue
    {
        private readonly Queue<Func<bool>> _pending = new();

        public int Count => _pending.Count;

        public void Enqueue(Func<bool> request) => _pending.Enqueue(request);

        public int Process(int maxCopies)
        {
            var copies = 0;
            while (copies < maxCopies && _pending.Count > 0)
            {
                if (_pending.Dequeue()())
                    copies++;
            }

            return copies;
        }
    }
}
