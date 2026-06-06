using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests;

public class ApiResultTests
{
    [Fact]
    public void Ok_Default_SetsSuccessAndCode200()
    {
        var r = ApiResult.Ok();

        r.Success.Should().BeTrue();
        r.Code.Should().Be(200);
        r.Message.Should().BeNull();
        r.Data.Should().BeNull();
    }

    [Fact]
    public void Ok_WithData_SetsData()
    {
        var r = ApiResult.Ok(data: new { x = 1 });

        r.Data.Should().NotBeNull();
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Ok_WithMessage_SetsMessage()
    {
        var r = ApiResult.Ok("成功");

        r.Message.Should().Be("成功");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void Error_SetsSuccessFalseAndCode()
    {
        var r = ApiResult.Error(4001, "错误信息");

        r.Success.Should().BeFalse();
        r.Code.Should().Be(4001);
        r.Message.Should().Be("错误信息");
    }
}
