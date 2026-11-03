namespace Foundation.Localization
{
    public interface ILocalePreferenceStore
    {
        bool TryLoad(out LocaleId locale);
        void Save(LocaleId locale);
    }
}
