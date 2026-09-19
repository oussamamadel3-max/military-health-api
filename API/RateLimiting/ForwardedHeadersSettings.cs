using System.Net;
using Microsoft.Extensions.Options;

namespace API.RateLimiting;

public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public List<string> KnownProxies { get; set; } = new();
    public List<string> KnownIPNetworks { get; set; } = new();
}

public sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        var errors = new List<string>();

        foreach (var proxy in options.KnownProxies)
        {
            if (string.IsNullOrWhiteSpace(proxy))
            {
                errors.Add("ForwardedHeaders:KnownProxies contains an empty value.");
                continue;
            }

            if (!IPAddress.TryParse(proxy.Trim(), out _))
                errors.Add($"ForwardedHeaders:KnownProxies value '{proxy}' is not a valid IP address.");
        }

        foreach (var network in options.KnownIPNetworks)
        {
            if (string.IsNullOrWhiteSpace(network))
            {
                errors.Add("ForwardedHeaders:KnownIPNetworks contains an empty value.");
                continue;
            }

            if (!System.Net.IPNetwork.TryParse(network.Trim(), out _))
                errors.Add($"ForwardedHeaders:KnownIPNetworks value '{network}' is not a valid CIDR (System.Net.IPNetwork).");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
