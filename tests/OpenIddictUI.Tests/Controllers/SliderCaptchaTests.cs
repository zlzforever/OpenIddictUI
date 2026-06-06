using System.Reflection;
using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests.Controllers;

public class SliderCaptchaTests
{
    /// <summary>
    /// 通过反射调用 CaptchaController 的 GenerateSliderImage 静态方法
    /// </summary>
    [Fact]
    public void GenerateSliderImage_ReturnsValidJpegBytes()
    {
        var method = typeof(CaptchaController).GetMethod("GenerateSliderImage",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("GenerateSliderImage 应为 private static 方法");

        var bytes = (byte[])method!.Invoke(null, [150])!;

        bytes.Should().NotBeNullOrEmpty();
        bytes.Length.Should().BeGreaterThan(100, "JPEG 图片至少有几百字节");

        // JPEG 文件头是 FF D8
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(150)]
    [InlineData(280)]
    public void GenerateSliderImage_DifferentPositions_AllValid(int notchX)
    {
        var method = typeof(CaptchaController).GetMethod("GenerateSliderImage",
            BindingFlags.NonPublic | BindingFlags.Static);
        var bytes = (byte[])method!.Invoke(null, [notchX])!;
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
        bytes.Length.Should().BeGreaterThan(500);
    }
}
