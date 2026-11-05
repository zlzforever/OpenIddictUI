# Temporary read-only tokens

Issuance is disabled by default. `POST /api/temporary-tokens` accepts a local, valid JWT access token **only** in the Bearer authorization header. A request body of `{}` inherits all source scopes; `{"scopes":["api1"]}` selects a non-empty subset. Success returns the usual `ApiResult`, with `data.accessToken`, `data.tokenType`, `data.expiresAt` (UTC) and `data.scopes`. No refresh token is returned and the response is marked `no-store` / `no-cache`. Missing or invalid Bearer is 401, invalid scope selection is 400, and a token or destination that cannot be shared is 403. Do not put a token in a URL, command history, logs or unencrypted communication.

The token expires no later than 300 seconds after issue or the source expiration, whichever is earlier. It can be used multiple times and is itself ineligible for another exchange. It is a transferable bearer credential: share it only with trusted parties. There is no immediate revocation guarantee. In particular, the new token does **not** inherit the source's OpenIddict token or authorization database entry; logout, account disablement or source/authorization revocation does not reliably revoke it. The original private `oi_tkn_id` and `oi_au_id` are intentionally excluded, not reused. Supported presenter state is retained; malformed or unknown private `oi_` fields, proof-of-possession `cnf`, duplicate JSON properties and preexisting `token_use` are rejected. Source signature, issuer, type and lifetime are verified by the OpenIddict validation scheme before the same JWT's payload is read. Some legacy tokens with unsupported scope structures or unrecognized protocol state cannot be shared.

## Enabling a target

Configure `OpenIddict:Issuer` to the public, stable issuer URI. The following is illustrative JSON configuration, **not a safe production authorization decision**:

```json
{
  "TemporaryTokens": {
    "Scopes": { "api1": ["api1"] },
    "Audiences": {
      "api1": {
        "Enabled": true,
        "IndependentClaims": ["name", "role", "client_id"]
      }
    }
  }
}
```

Every selected scope must map to at least one configured audience also present in the source. If any scope fails, the request is rejected instead of silently shrinking it. The emitted audiences are the intersection of those mappings and the source audiences. Because each emitted audience receives every selected scope, the request is also rejected if any resulting scope/audience pair is not explicitly allowed; request a smaller subset instead. Each matched audience must have an enabled policy. Every preserved claim other than protocol fields and `sub` must appear in **each** selected audience's `IndependentClaims`; missing policy, unknown claims and invalid configuration deny issuance. The list is an administrator's explicit attestation that each permitted claim's values and structure are safe to retain for **every selected scope**, and that the receiver does not derive broader authorization from it. A claim name by itself does not establish this fact: examine roles, tenant IDs, nested permissions and custom target selectors with each receiver's owner before allowing them. If no such guarantee can be made, do not add the claim. This applies even when all source scopes are inherited. Values and JSON structures are preserved rather than rewritten. `client_id`, `azp` and identity claims other than `sub` also require review.

Configuration keys are case-sensitive for policy comparison. Use simple audience/scope names that do not contain the configuration separator `:`. Keep all targets disabled until all services sharing each audience have been reviewed. At minimum every receiver must validate signature, issuer, audience, scope and lifetime with zero or appropriately bounded clock tolerance, recognize `token_use=temporary_readonly`, reject writes and unclassified operations by **business operation** (not just HTTP method), and retain its normal data/role authorization. A service that ignores the marker can still accept the token for writes. Never enable HTTP request/response body or authorization-header logging. OpenIddict trace logging can include tokens; keep its log level above Trace, and ensure reverse proxies and observability systems also redact credentials.

## Example API and rollout

The sample API uses `Authority` and optionally `ValidAudiences` (defaults to `api1`). It now requires correct issuer and audience, so previously accepted tokens with invalid or absent audience are rejected. HTTPS metadata is mandatory outside development; HTTP authority is accepted only in development. `/api/me` is explicitly classified read-only; `/api/test-write` is a harmless classified write demonstration, and `/api/unclassified` demonstrates default deny. A temporary token can call the first but gets 403 for the latter two; ordinary authorized access tokens retain their normal behavior.

For a local demonstration set the issuer, API authority and isolated test settings, configure an approved audience and reviewed claims, then send the request over a protected connection. For example, in a shell with silent `read`, the following reads the source from the terminal rather than placing it in shell arguments or history:

```bash
read -r -s SOURCE_TOKEN
curl --config - <<EOF
url = "https://issuer.example/api/temporary-tokens"
request = "POST"
header = "Authorization: Bearer $SOURCE_TOKEN"
header = "Content-Type: application/json"
data = "{\"scopes\":[\"api1\"]}"
EOF
unset SOURCE_TOKEN
```

The response contains a credential and should only be displayed or handled in a protected environment. To call the sample API, securely read the returned `accessToken` and use the same approach:

```bash
read -r -s TEMPORARY_TOKEN
curl --config - <<EOF
url = "https://api.example/api/me"
header = "Authorization: Bearer $TEMPORARY_TOKEN"
EOF
unset TEMPORARY_TOKEN
```

Replace the example URLs with the configured issuer and API. Do not use real credentials in tests, capture these requests in shell tracing, or forward the token to unreviewed services.

To stop new issuance, disable or remove all configured audience policies. Already issued JWTs remain usable until expiry; keep all receiver-side read-only enforcement enabled until at least the maximum token lifetime plus any receiver clock tolerance has elapsed. Removing configuration cannot recall copies already distributed.
