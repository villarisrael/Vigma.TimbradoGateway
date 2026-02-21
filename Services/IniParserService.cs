using TimbradoGateway.Infrastructure.Ini;


namespace TimbradoGateway.Services;

public interface IIniParserService
{
    IniCfdiDocument Parse(string ini);
}

public sealed class IniParserService : IIniParserService
{
    public IniCfdiDocument Parse(string ini)
    {
        // Esto asume que en Infrastructure/Ini/IniDocument.cs existe:
        // public static IniCfdiDocument Parse(string ini)
        return IniDocument.Parse(ini);
    }
}
