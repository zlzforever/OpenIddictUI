namespace OpenIddictUI.Sms;

public sealed record SendCodeRequest(
    string PhoneNumber,
    string CountryCode,
    string Code,
    string RateLimitKey,
    string Scenario);