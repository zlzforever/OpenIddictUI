namespace OpenIddictUI.Sms;

public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string code);
}
