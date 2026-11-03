using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial
{
    public sealed class TutorialChapterCatalog
    {
        private readonly ITutorialChapterSource _source;
        private readonly Dictionary<TutorialChapterId, TutorialChapterDefinition> _byId = new();
        private TutorialChapterDefinition[] _ordered = Array.Empty<TutorialChapterDefinition>();

        public bool IsInitialized { get; private set; }

        public IReadOnlyList<TutorialChapterDefinition> Chapters => _ordered;

        public TutorialChapterCatalog(ITutorialChapterSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            var chapters = await _source.GetChaptersAsync(cancellationToken);
            _byId.Clear();
            var ordered = new List<TutorialChapterDefinition>(chapters?.Count ?? 0);
            foreach (var chapter in chapters ?? Array.Empty<TutorialChapterDefinition>())
            {
                if (chapter == null)
                    throw new InvalidOperationException("Tutorial chapter source produced a null chapter.");
                if (!_byId.TryAdd(chapter.Id, chapter))
                    throw new InvalidOperationException($"Tutorial chapter '{chapter.Id}' is declared more than once.");

                ordered.Add(chapter);
            }

            ordered.Sort(Compare);
            _ordered = ordered.ToArray();
            IsInitialized = true;
        }

        public bool TryGet(TutorialChapterId chapterId, out TutorialChapterDefinition chapter)
            => _byId.TryGetValue(chapterId, out chapter);

        public TutorialChapterDefinition Get(TutorialChapterId chapterId)
            => _byId.TryGetValue(chapterId, out var chapter)
                ? chapter
                : throw new KeyNotFoundException($"Tutorial chapter '{chapterId}' is not registered.");

        internal static int Compare(TutorialChapterDefinition left, TutorialChapterDefinition right)
        {
            var byPriority = right.Priority.CompareTo(left.Priority);
            return byPriority != 0
                ? byPriority
                : string.CompareOrdinal(left.Id.Value, right.Id.Value);
        }
    }
}
