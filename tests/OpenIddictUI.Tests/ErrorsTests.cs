using OpenIddictUI.Controllers;

namespace OpenIddictUI.Tests;

public class ErrorsTests
{
    [Fact]
    public void ResultProperties_HaveCorrectCodes()
    {
        Errors.InvalidCaptcha.Code.Should().Be(4021);
        Errors.IncorrectPassword.Code.Should().Be(4022);
        Errors.SliderRequired.Code.Should().Be(4023);
        Errors.ConsentInvalid.Code.Should().Be(4024);
        Errors.ClientDisabled.Code.Should().Be(4025);
        Errors.InvalidCredentialsResult.Code.Should().Be(Errors.InvalidCredentials);
        Errors.UserLockedOutResult.Code.Should().Be(Errors.UserLockedOut);
        Errors.UserNotExistResult.Code.Should().Be(Errors.UserNotExist);
        Errors.SliderCaptchaExpiredResult.Code.Should().Be(Errors.SliderCaptchaExpired);
        Errors.SliderCaptchaFailedResult.Code.Should().Be(Errors.SliderCaptchaFailed);
        Errors.UserNotAllowedResult.Code.Should().Be(Errors.UserNotAllowed);
        Errors.PasswordValidateFailedResult.Code.Should().Be(Errors.PasswordValidateFailed);
        Errors.ChangePasswordFailedResult.Code.Should().Be(Errors.ChangePasswordFailed);
        Errors.SendSmsFailedResult.Code.Should().Be(Errors.SendSmsFailed);
        Errors.InvalidParams.Code.Should().Be(400);
        Errors.NotAuthenticated.Code.Should().Be(401);
        Errors.InvalidRequest.Code.Should().Be(400);
    }

    [Fact]
    public void AllResultProperties_NotSuccess()
    {
        var props = typeof(Errors).GetProperties()
            .Where(p => p.PropertyType == typeof(ApiResult));

        foreach (var p in props)
        {
            var r = (ApiResult)p.GetValue(null)!;
            r.Success.Should().BeFalse($"{p.Name} 应设置 Success=false");
            r.Code.Should().NotBe(200, $"{p.Name} 应设置非200状态码");
            r.Message.Should().NotBeNullOrEmpty($"{p.Name} 应有错误消息");
        }
    }

    [Fact]
    public void KeyMessages_AreCorrect()
    {
        var expected = new Dictionary<ApiResult, string>
        {
            [Errors.InvalidCaptcha] = "验证码不正确",
            [Errors.IncorrectPassword] = "密码不正确",
            [Errors.SliderRequired] = "请先完成安全验证",
            [Errors.InvalidCredentialsResult] = "用户名或密码错误",
            [Errors.UserLockedOutResult] = "用户被锁定",
            [Errors.UserNotExistResult] = "用户不存在",
            [Errors.PasswordValidateFailedResult] = "密码不符合安全要求，请先修改密码",
            [Errors.ChangePasswordFailedResult] = "重置密码失败",
            [Errors.SendSmsFailedResult] = "短信发送失败",
            [Errors.SliderCaptchaExpiredResult] = "验证已过期，请重试",
            [Errors.SliderCaptchaFailedResult] = "验证失败，请重试",
            [Errors.NotAuthenticated] = "Not authenticated",
            [Errors.InvalidRequest] = "请求不合法",
            [Errors.InvalidAntiForgery] = "Invalid anti-forgery token",
            [Errors.InvalidParams] = "参数错误",
            [Errors.NoConsentRequestMatchingResult] = "Consent request expired",
            [Errors.VerifyCodeLoginFailed] = "登录失败",
            [Errors.VerifyCodeIncorrectResult] = "验证码不正确",
            [Errors.ResetPasswordByPhoneFailed] = "重置密码失败",
        };

        foreach (var (result, expectedMsg) in expected)
        {
            result.Message.Should().Be(expectedMsg, $"结果 {result.Code} 的消息应为 '{expectedMsg}'");
        }
    }

    [Fact]
    public void AllNumericConstants_AreUnique()
    {
        var codes = typeof(Errors).GetFields()
            .Where(f => f.FieldType == typeof(int))
            .Select(f => (int)f.GetValue(null)!)
            .ToList();

        codes.Should().OnlyHaveUniqueItems("所有错误码应唯一");

        // 所有 code 应在 4001-4999 或 400/401 范围内
        foreach (var c in codes)
        {
            (c >= 4001 || c == 400 || c == 401).Should().BeTrue(
                $"错误码 {c} 应在 400、401 或 4001-4999 范围内");
        }
    }
}
