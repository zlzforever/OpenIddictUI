using Microsoft.Extensions.Logging;
using Moq;
using OpenIddictUI.Sms;

namespace OpenIddictUI.Tests.Sms;

public class SmsSendersTests
{
    [Fact]
    public async Task ConsoleSmsSender_LogsAndReturns()
    {
        var logger = Mock.Of<ILogger<ConsoleSmsSender>>();
        var sender = new ConsoleSmsSender(logger);

        await sender.Invoking(s => s.SendAsync("+86 13800138000", "123456"))
            .Should().NotThrowAsync();
    }
}
