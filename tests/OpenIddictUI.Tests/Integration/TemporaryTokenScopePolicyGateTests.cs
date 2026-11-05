using System.Text.Json;

namespace OpenIddictUI.Tests.Integration;

// Feasibility model, not a production authorization implementation. A receiver's
// owner must attest the semantics of every configured independent claim.
public class TemporaryTokenScopePolicyGateTests
{
    private static bool CanShare(JsonElement source, string[] requested, string[] sourceScopes,
        string[] sourceAudiences, Dictionary<string, string[]> targets, Dictionary<string, string[]> independentClaims)
    {
        var requestedSet = requested.ToHashSet(StringComparer.Ordinal);
        if (requested.Length == 0 || requestedSet.Count != requested.Length ||
            !requestedSet.IsSubsetOf(sourceScopes.ToHashSet(StringComparer.Ordinal)))
        {
            return false;
        }
        var audiences = sourceAudiences.ToHashSet(StringComparer.Ordinal);
        var selectedAudiences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in requested)
        {
            if (!targets.TryGetValue(scope, out var configured))
            {
                return false;
            }
            var matches = configured.Where(audiences.Contains).ToArray();
            if (matches.Length == 0)
            {
                return false;
            }
            selectedAudiences.UnionWith(matches);
        }
        string[] protocol = ["sub", "iss", "aud", "scope", "iat", "nbf", "exp", "jti"];
        foreach (var audience in selectedAudiences)
        {
            if (!independentClaims.TryGetValue(audience, out var safe))
            {
                return false;
            }
            foreach (var claim in source.EnumerateObject())
            {
                if (!protocol.Contains(claim.Name, StringComparer.Ordinal) &&
                    !safe.Contains(claim.Name, StringComparer.Ordinal))
                {
                    return false;
                }
            }
        }
        return true;
    }

    [Fact]
    public void NarrowingRejectsUnclassifiedPermissionsForEverySelectedAudience()
    {
        using var source = JsonDocument.Parse("""{"sub":"alice","role":"admin","permissions":{"api2":"write"}}""");
        var targets = new Dictionary<string, string[]> { ["api1"] = ["a"], ["api2"] = ["b"] };
        var policy = new Dictionary<string, string[]> { ["a"] = ["role"] };
        Assert.False(CanShare(source.RootElement, ["api1"], ["api1", "api2"], ["a", "b"], targets, policy));
        // The receiver explicitly attests that these claims are scope-independent.
        policy["a"] = ["role", "permissions"];
        Assert.True(CanShare(source.RootElement, ["api1"], ["api1", "api2"], ["a", "b"], targets, policy));
        Assert.False(CanShare(source.RootElement, ["api1", "api2"], ["api1", "api2"], ["a", "b"], targets, policy));
        Assert.False(CanShare(source.RootElement, ["api1"], ["api1", "api2"], ["b"], targets, policy));
        Assert.False(CanShare(source.RootElement, ["api1", "api1"], ["api1", "api2"], ["a"], targets, policy));
        Assert.False(CanShare(source.RootElement, ["API1"], ["api1", "api2"], ["a"], targets, policy));
        Assert.False(CanShare(source.RootElement, [], ["api1", "api2"], ["a"], targets, policy));
    }
}
