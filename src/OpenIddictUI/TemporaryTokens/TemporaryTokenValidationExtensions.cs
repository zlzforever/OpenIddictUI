using Microsoft.AspNetCore;
using OpenIddict.Validation;

namespace OpenIddictUI.TemporaryTokens;

public static class TemporaryTokenValidationExtensions
{
    public static OpenIddictValidationBuilder RejectTemporaryTokenProofOfPossession(this OpenIddictValidationBuilder builder)
    {
        // OpenIddict 7.7 may throw on unsupported cnf rather than reject it. This
        // runs after signature validation and only changes this Bearer-only route.
        builder.AddEventHandler<OpenIddictValidationEvents.ValidateTokenContext>(events => events
            .UseInlineHandler(context =>
            {
                var request = context.Transaction.GetHttpRequest();
                if (request is not null &&
                    string.Equals(request.Path.Value?.TrimEnd('/'), "/api/temporary-tokens", StringComparison.OrdinalIgnoreCase) &&
                    context.Principal?.HasClaim(claim => claim.Type == "cnf") == true)
                {
                    context.Reject(error: "invalid_token");
                }
                return default;
            })
            .SetOrder(OpenIddictValidationHandlers.Protection.ValidateProofOfPossession.Descriptor.Order - 1));
        return builder;
    }
}
