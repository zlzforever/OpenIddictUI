using System.Reflection;
using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests.Controllers;

public class CaptchaGenerationTests
{
    [Fact]
    public void GenerateCode_ReturnsCorrectLength()
    {
        var method = typeof(CaptchaController).GetMethod("GenerateCode",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        for (var len = 1; len <= 9; len++)
        {
            var code = (string)method!.Invoke(null, [len])!;
            code.Should().HaveLength(len);
        }
    }

    [Fact]
    public void GenerateCode_OnlyUsesValidChars()
    {
        var method = typeof(CaptchaController).GetMethod("GenerateCode",
            BindingFlags.NonPublic | BindingFlags.Static);
        const string validChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        for (var i = 0; i < 30; i++)
        {
            var code = (string)method!.Invoke(null, [6])!;
            code.All(c => validChars.Contains(c)).Should().BeTrue($"'{code}' contains invalid chars");
        }
    }

    [Fact]
    public void GenerateCode_NotAllSame()
    {
        var method = typeof(CaptchaController).GetMethod("GenerateCode",
            BindingFlags.NonPublic | BindingFlags.Static);
        var codes = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            codes.Add((string)method!.Invoke(null, [4])!);
        }
        codes.Count.Should().BeGreaterThan(1, "随机生成的验证码不应完全相同");
    }

    [Fact]
    public void GenerateCode_ReturnsString()
    {
        var method = typeof(CaptchaController).GetMethod("GenerateCode",
            BindingFlags.NonPublic | BindingFlags.Static);
        var code = method!.Invoke(null, [4]);
        code.Should().BeOfType<string>();
        ((string)code!).Should().NotBeNullOrWhiteSpace();
    }
}
