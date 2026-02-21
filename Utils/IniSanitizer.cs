using System.Text.RegularExpressions;

namespace Vigma.TimbradoGateway.Utils;

public static class IniSanitizer
{
    public static string Sanitize(string ini)
    {
        if (string.IsNullOrWhiteSpace(ini)) return ini;

        // Quita pass= en [conf] y [PAC]
        ini = Regex.Replace(ini, @"(?im)^(pass\s*=\s*).*$", "$1****");
        return ini;
    }
}
