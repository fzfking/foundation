namespace Foundation.Tutorial
{
    public sealed class TutorialChapterSnapshot
    {
        public string ChapterId;
        public int Status;
        public string CurrentStepId;
        public bool IsPending;
        public string[] CompletedStepIds;
    }
    
    public sealed class TutorialSnapshot
    {
        public TutorialChapterSnapshot[] Chapters;
    }
}
