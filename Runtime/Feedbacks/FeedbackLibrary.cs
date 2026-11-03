using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Feedbacks
{
    [CreateAssetMenu(menuName = "Foundation/Feedbacks/Library", fileName = "FeedbackLibrary")]
    public sealed class FeedbackLibrary : ScriptableObject
    {
        [SerializeField] private List<FeedbackDefinition> definitions = new();

        public IReadOnlyList<FeedbackDefinition> Definitions => definitions;
    }
}
