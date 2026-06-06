namespace OpenIddictUI.Tests;

public class UtilTests
{
    [Fact]
    public void CaptchaId_Constant_IsCorrect()
    {
        Util.CaptchaId.Should().Be("CaptchaId");
    }

    [Fact]
    public void CaptchaImageKey_FormatsCorrectly()
    {
        var key = string.Format(Util.CaptchaImageKey, "abc123");
        key.Should().Be("Captcha:Image:abc123");
    }

    [Fact]
    public void CaptchaSliderKey_FormatsCorrectly()
    {
        var key = string.Format(Util.CaptchaSliderKey, "xyz789");
        key.Should().Be("Captcha:Slider:xyz789");
    }

    [Fact]
    public void CaptchaSliderVerified_FormatsCorrectly()
    {
        var key = string.Format(Util.CaptchaSliderVerified, "def456");
        key.Should().Be("Captcha:SliderVerified:def456");
    }

    [Fact]
    public void SmsRateLimit_FormatsCorrectly()
    {
        var key = string.Format(Util.SmsRateLimit, "13800138000");
        key.Should().Be("SMS:RateLimit:13800138000");
    }

    [Fact]
    public void RegisterCode_FormatsCorrectly()
    {
        var key = string.Format(Util.RegisterCode, "13800138000");
        key.Should().Be("Register:Code:13800138000");
    }

    [Fact]
    public void PurposeLogin_Constant_IsCorrect()
    {
        Util.PurposeLogin.Should().Be("Login");
    }

    [Fact]
    public void PurposeRegister_Constant_IsCorrect()
    {
        Util.PurposeRegister.Should().Be("Register");
    }
}
