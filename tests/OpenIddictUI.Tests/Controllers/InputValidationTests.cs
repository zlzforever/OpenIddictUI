using System.ComponentModel.DataAnnotations;
using OpenIddictUI.Controllers.Input;

namespace OpenIddictUI.Tests.Controllers;

public class InputValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(model);
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }

    [Fact]
    public void LoginInput_Valid_Succeeds()
    {
        var m = new LoginInput { Username = "testuser", Password = "P@ss1234", CaptchaCode = "ABC", Button = "login" };
        var errors = Validate(m);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void LoginInput_EmptyUsername_FailsValidation()
    {
        var m = new LoginInput { Username = "", Password = "P@ss1234" };
        var errors = Validate(m);
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.MemberNames.Contains("Username"));
    }

    [Fact]
    public void LoginInput_EmptyPassword_FailsValidation()
    {
        var m = new LoginInput { Username = "test", Password = "" };
        var errors = Validate(m);
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    [Fact]
    public void LoginInput_LongUsername_FailsValidation()
    {
        var m = new LoginInput { Username = new string('x', 51), Password = "pwd" };
        var errors = Validate(m);
        errors.Should().Contain(e => e.MemberNames.Contains("Username"));
    }

    [Fact]
    public void LoginInput_LongCaptcha_FailsValidation()
    {
        var m = new LoginInput { Username = "u", Password = "p", CaptchaCode = new string('x', 11) };
        var errors = Validate(m);
        errors.Should().Contain(e => e.MemberNames.Contains("CaptchaCode"));
    }

    [Fact]
    public void LoginByCodeInput_Valid_Succeeds()
    {
        var m = new LoginByCodeInput { PhoneNumber = "13800138000", VerifyCode = "123456", Button = "login" };
        var errors = Validate(m);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void LoginByCodeInput_EmptyPhone_FailsValidation()
    {
        var m = new LoginByCodeInput { PhoneNumber = "", VerifyCode = "123456" };
        var errors = Validate(m);
        errors.Should().Contain(e => e.MemberNames.Contains("PhoneNumber"));
    }

    [Fact]
    public void SendCodeInput_Valid_Succeeds()
    {
        var m = new SendCodeInput { PhoneNumber = "13800138000", CaptchaCode = "ABC" };
        var errors = Validate(m);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void SendCodeInput_EmptyPhone_FailsValidation()
    {
        var m = new SendCodeInput { PhoneNumber = "" };
        var errors = Validate(m);
        errors.Should().Contain(e => e.MemberNames.Contains("PhoneNumber"));
    }

    [Fact]
    public void SendCodeInput_CaptchaCodeNull_StillValid()
    {
        var m = new SendCodeInput { PhoneNumber = "13800138000", CaptchaCode = null };
        var errors = Validate(m);
        errors.Should().BeEmpty(); // CaptchaCode 没有 [Required]
    }

    [Fact]
    public void ResetPasswordByOriginPasswordInput_Valid_Succeeds()
    {
        var m = new ResetPasswordByOriginPasswordInput
        {
            UserName = "user", OldPassword = "old1", NewPassword = "new1", ConfirmNewPassword = "new1"
        };
        var errors = Validate(m);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ResetPasswordByOriginPasswordInput_PasswordMismatch_FailsValidation()
    {
        var m = new ResetPasswordByOriginPasswordInput
        {
            UserName = "user", OldPassword = "old1", NewPassword = "new1", ConfirmNewPassword = "new2"
        };
        var errors = Validate(m);
        errors.Should().Contain(e => e.MemberNames.Contains("ConfirmNewPassword"));
    }

    [Fact]
    public void ResetPasswordByOriginPasswordInput_EmptyFields_FailsValidation()
    {
        var m = new ResetPasswordByOriginPasswordInput();
        var errors = Validate(m);
        errors.Count.Should().BeGreaterThanOrEqualTo(3); // UserName, NewPassword, OldPassword are required
    }

    [Fact]
    public void ResetPasswordByPhoneInput_Valid_Succeeds()
    {
        var m = new ResetPasswordByPhoneInput
        {
            PhoneNumber = "13800138000", VerifyCode = "123456", NewPassword = "new1", ConfirmNewPassword = "new1"
        };
        var errors = Validate(m);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ResetPasswordByPhoneInput_EmptyPhone_FailsValidation()
    {
        var m = new ResetPasswordByPhoneInput { PhoneNumber = "", VerifyCode = "123", NewPassword = "n", ConfirmNewPassword = "n" };
        var errors = Validate(m);
        errors.Should().Contain(e => e.MemberNames.Contains("PhoneNumber"));
    }
}
