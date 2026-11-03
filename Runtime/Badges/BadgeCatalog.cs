using System;
using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Badges
{
    [Serializable]
    public sealed class BadgeDefinition
    {
        [SerializeField] private BadgeId id;
        [SerializeField] private BadgeId parent;
        [SerializeField] private BadgeAggregation aggregation;

        public BadgeId Id => id;

        public BadgeId Parent => parent;

        public BadgeAggregation Aggregation => aggregation;

        public BadgeDefinition(BadgeId id, BadgeId parent = default, BadgeAggregation aggregation = BadgeAggregation.None)
        {
            this.id = id;
            this.parent = parent;
            this.aggregation = aggregation;
        }
    }

    [CreateAssetMenu(menuName = "Foundation/Badges/Catalog", fileName = "BadgeCatalog")]
    public sealed class BadgeCatalog : ScriptableObject
    {
        [SerializeField] private List<BadgeDefinition> definitions = new();
        public IReadOnlyList<BadgeDefinition> Definitions => definitions;

        public static BadgeCatalog Create(IEnumerable<BadgeDefinition> items)
        {
            var catalog = CreateInstance<BadgeCatalog>();
            catalog.definitions.AddRange(items);
            return catalog;
        }
    }
}
