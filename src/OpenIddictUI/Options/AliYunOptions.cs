namespace OpenIddictUI.Options;

public class AliYunOptions
{
    public string AccessKey { get; set; }
    public string Secret { get; set; }
    public string Endpoint { get; set; }
    public SmsOptions Sms { get; set; }

    public class SmsOptions
    {
        public string SignName { get; set; }
        public Dictionary<string, string> Templates { get; set; }
    }
}