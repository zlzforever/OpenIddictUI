namespace OpenIddictUI.Sms;

public class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public static string Name => "Console";

    public Task SendAsync(string phoneNumber, string code)
    {
        logger.LogInformation("[SMS] To: {Phone}, Code: {Code}", phoneNumber, code);
        return Task.CompletedTask;
    }
}