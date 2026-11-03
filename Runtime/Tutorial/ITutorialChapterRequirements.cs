namespace Foundation.Tutorial
{
    public interface ITutorialChapterRequirements
    {
        Requirements.IRequirement GetRequirement(TutorialChapterId chapterId);
    }
}
