namespace OpenBI.Converters.Interfaces
{
    public interface IOpenBIConverterFactory
    {
        IOpenBIConverter CreateOpenBIConverter(string assetType);
    }
}
