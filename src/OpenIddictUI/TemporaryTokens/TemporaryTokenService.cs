using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace OpenIddictUI.TemporaryTokens;

public sealed record TemporaryTokenResult(int Status, string? Token = null, DateTimeOffset? ExpiresAt = null, string[]? Scopes = null);

// Only called after the local validation scheme authenticates the exact bearer string.
public sealed class TemporaryTokenService(IOptionsMonitor<OpenIddictServerOptions> server, IConfiguration configuration)
{
    private static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
    {
        "iss", "iat", "nbf", "exp", "jti", "scope", "aud", "oi_tkn_id", "oi_au_id"
    };
    private static readonly HashSet<string> Protocol = new(StringComparer.Ordinal)
    {
        "sub", "iss", "iat", "nbf", "exp", "jti", "scope", "aud", "oi_tkn_id", "oi_au_id", "oi_prst"
    };

    public TemporaryTokenResult Create(string token, JsonElement request)
    {
        try
        {
            return CreateCore(token, request);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return new(403);
        }
    }

    private TemporaryTokenResult CreateCore(string token, JsonElement request)
    {
        if (HasDuplicates(request) || request.ValueKind != JsonValueKind.Object)
        {
            return new(400);
        }
        string[]? requested = null;
        if (request.TryGetProperty("scopes", out var scopeRequest))
        {
            if (scopeRequest.ValueKind != JsonValueKind.Array || scopeRequest.GetArrayLength() == 0 ||
                scopeRequest.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || !ValidScope(item.GetString())))
            {
                return new(400);
            }
            requested = scopeRequest.EnumerateArray().Select(item => item.GetString()!).ToArray();
            if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
            {
                return new(400);
            }
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return new(403);
        }
        using var headerDocument = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
        if (headerDocument.RootElement.ValueKind != JsonValueKind.Object || HasDuplicates(headerDocument.RootElement))
        {
            return new(403);
        }
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
        var payload = document.RootElement;
        if (payload.ValueKind != JsonValueKind.Object || HasDuplicates(payload) || payload.TryGetProperty("token_use", out _) ||
            payload.TryGetProperty("cnf", out _) || !CheckPrivateClaims(payload))
        {
            return new(403);
        }

        if (!payload.TryGetProperty("scope", out var sourceScope) ||
            !payload.TryGetProperty("aud", out var sourceAudience) ||
            !payload.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var sourceExpiration))
        {
            return new(403);
        }
        var sourceScopes = sourceScope.ValueKind switch
        {
            JsonValueKind.String => (sourceScope.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            JsonValueKind.Array when sourceScope.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) =>
                sourceScope.EnumerateArray().SelectMany(item => (item.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray(),
            _ => []
        };
        var audiences = ReadAudiences(sourceAudience);
        if (sourceScopes.Length == 0 || sourceScopes.Any(scope => !ValidScope(scope)) ||
            sourceScopes.Distinct(StringComparer.Ordinal).Count() != sourceScopes.Length || audiences.Length == 0)
        {
            return new(403);
        }
        var selectedScopes = requested ?? sourceScopes;
        if (selectedScopes.Except(sourceScopes, StringComparer.Ordinal).Any())
        {
            return new(400);
        }

        var allowedAudiences = new HashSet<string>(StringComparer.Ordinal);
        var scopeTargets = new List<HashSet<string>>();
        foreach (var scope in selectedScopes)
        {
            var section = configuration.GetSection("TemporaryTokens:Scopes").GetChildren()
                .SingleOrDefault(child => string.Equals(child.Key, scope, StringComparison.Ordinal));
            if (section is null || !TryReadArray(section, false, out var targets))
            {
                return new(403);
            }
            scopeTargets.Add(targets.ToHashSet(StringComparer.Ordinal));
            var matches = targets.Intersect(audiences, StringComparer.Ordinal).ToArray();
            if (matches.Length == 0)
            {
                return new(403);
            }
            allowedAudiences.UnionWith(matches);
        }
        // A single JWT presents every scope to every audience. Do not emit a
        // scope/audience pair that the administrator has not actually allowed.
        if (scopeTargets.Any(targets => !allowedAudiences.IsSubsetOf(targets)))
        {
            return new(403);
        }

        // Each receiver explicitly attests every preserved non-protocol claim is
        // safe for all scopes it accepts; unknown claims deny instead of disappearing.
        foreach (var audience in allowedAudiences)
        {
            var policy = configuration.GetSection("TemporaryTokens:Audiences").GetChildren()
                .SingleOrDefault(child => string.Equals(child.Key, audience, StringComparison.Ordinal));
            if (policy is null || !bool.TryParse(policy["Enabled"], out var enabled) || !enabled)
            {
                return new(403);
            }
            if (!TryReadArray(policy.GetSection("IndependentClaims"), true, out var independent))
            {
                return new(403);
            }
            if (payload.EnumerateObject().Any(property => !Protocol.Contains(property.Name) && !independent.Contains(property.Name, StringComparer.Ordinal)))
            {
                return new(403);
            }
        }

        var options = server.CurrentValue;
        if (options.Issuer is null || options.SigningCredentials.Count == 0)
        {
            return new(403);
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiration = Math.Min(now + 300, sourceExpiration);
        if (expiration <= now)
        {
            return new(403);
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                if (!Metadata.Contains(property.Name))
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteString("iss", options.Issuer.AbsoluteUri);
            writer.WriteNumber("iat", now);
            writer.WriteNumber("nbf", now);
            writer.WriteNumber("exp", expiration);
            writer.WriteString("jti", Guid.NewGuid().ToString());
            writer.WriteString("scope", string.Join(' ', selectedScopes));
            writer.WritePropertyName("aud");
            writer.WriteStartArray();
            foreach (var audience in allowedAudiences.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(audience);
            }
            writer.WriteEndArray();
            writer.WriteString("token_use", "temporary_readonly");
            writer.WriteEndObject();
        }
        var signed = new JsonWebTokenHandler().CreateToken(Encoding.UTF8.GetString(stream.ToArray()), options.SigningCredentials[0],
            new Dictionary<string, object> { ["typ"] = "at+jwt" });
        return new(200, signed, DateTimeOffset.FromUnixTimeSeconds(expiration), selectedScopes);
    }

    private static bool CheckPrivateClaims(JsonElement payload)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (!property.Name.StartsWith("oi_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (property.Name is not ("oi_tkn_id" or "oi_au_id" or "oi_prst") ||
                property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                return false;
            }
        }
        return true;
    }

    private static string[] ReadAudiences(JsonElement audience)
    {
        if (audience.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(audience.GetString()))
        {
            return [audience.GetString()!];
        }
        if (audience.ValueKind != JsonValueKind.Array || audience.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
        {
            return [];
        }
        var values = audience.EnumerateArray().Select(item => item.GetString()!).ToArray();
        return values.Distinct(StringComparer.Ordinal).Count() == values.Length ? values : [];
    }

    private static bool TryReadArray(IConfigurationSection section, bool optional, out string[] values)
    {
        values = [];
        if (section.Value is not null)
        {
            return false;
        }
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return optional;
        }
        for (var index = 0; index < children.Length; index++)
        {
            if (children[index].Key != index.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                string.IsNullOrWhiteSpace(children[index].Value) || children[index].GetChildren().Any())
            {
                return false;
            }
        }
        values = children.Select(child => child.Value!).ToArray();
        return values.Distinct(StringComparer.Ordinal).Count() == values.Length;
    }

    private static bool ValidScope(string? value) => !string.IsNullOrEmpty(value) &&
        value.All(character => character == '!' || character is >= '#' and <= '[' || character is >= ']' and <= '~');

    private static bool HasDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicates);
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name) || HasDuplicates(property.Value));
    }
}
