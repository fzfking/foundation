namespace Foundation.Localization
{
    /// <summary>One locale's table as it is exported and shipped: parallel key and value arrays.</summary>
    public sealed class LocalizationTableData
    {
        public string Locale;
        public string[] Keys;
        public string[] Values;
    }
}
