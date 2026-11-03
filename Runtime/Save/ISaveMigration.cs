namespace Foundation.Save
{
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        SaveData Migrate(SaveData data);
    }
}
