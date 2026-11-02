using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddictUI.Controllers;
using OpenIddictUI.Controllers.Input;
using OpenIddictUI.Identity;
using OpenIddictUI.Options;
using OpenIddictUI.Sms;

namespace OpenIddictUI.Tests.Controllers;

public class AccountIdentifierTrimTests
{
    [Fact]
    public async Task Login_TrimsUsername_ButKeepsPasswordUnchanged()
    {
        var (controller, users, signIn) = CreateController();
        var user = new User { UserName = "alice" };
        users.Setup(manager => manager.FindByNameAsync("alice")).ReturnsAsync(user);
        signIn.Setup(manager => manager.PasswordSignInAsync(user, " secret ", false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        await controller.Login(new LoginInput { Username = " alice ", Password = " secret " });

        users.Verify(manager => manager.FindByNameAsync("alice"), Times.Once);
        signIn.Verify(manager => manager.PasswordSignInAsync(user, " secret ", false, true), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_TrimsUsername_ButDoesNotChangeOldPassword()
    {
        var (controller, users, _) = CreateController();
        var user = new User { UserName = "alice" };
        users.Setup(manager => manager.FindByNameAsync("alice")).ReturnsAsync(user);
        users.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(false);
        users.Setup(manager => manager.CheckPasswordAsync(user, " old password ")).ReturnsAsync(false);

        await controller.ResetPasswordByOriginPassword(new ResetPasswordByOriginPasswordInput
        {
            UserName = " alice ", OldPassword = " old password ",
            NewPassword = "new", ConfirmNewPassword = "new"
        });

        users.Verify(manager => manager.FindByNameAsync("alice"), Times.Once);
        users.Verify(manager => manager.CheckPasswordAsync(user, " old password "), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordBySms_TrimsPhoneNumberBeforeQuery()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PhoneTestDbContext>().UseSqlite(connection).Options;
        await using var db = new PhoneTestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User { Id = "1", PhoneNumber = "13800138000" });
        await db.SaveChangesAsync();

        var (controller, users, _) = CreateController();
        users.Setup(manager => manager.Users).Returns(db.Users);
        users.Setup(manager => manager.IsLockedOutAsync(It.IsAny<User>())).ReturnsAsync(true);

        var result = await controller.ResetPassword(new ResetPasswordByPhoneInput
        {
            PhoneNumber = " 13800138000 ", VerifyCode = "123456",
            NewPassword = "new", ConfirmNewPassword = "new"
        });

        ((OkObjectResult)result).Value.Should().BeOfType<ApiResult>()
            .Which.Code.Should().Be(Errors.UserLockedOut);
        users.Verify(manager => manager.IsLockedOutAsync(It.Is<User>(user => user.PhoneNumber == "13800138000")),
            Times.Once);
    }

    private static (AccountController Controller, Mock<UserManager<User>> Users, Mock<SignInManager<User>> SignIn)
        CreateController()
    {
        var users = new Mock<UserManager<User>>(Mock.Of<IUserStore<User>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var signIn = new Mock<SignInManager<User>>(users.Object, Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(), null!, null!, null!, null!);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(host => host.EnvironmentName).Returns(Environments.Development);
        var services = new ServiceCollection().AddSingleton<IHostEnvironment>(environment.Object)
            .BuildServiceProvider();
        var controller = new AccountController(signIn.Object, users.Object, Mock.Of<HybridCache>(),
            Mock.Of<IPasswordValidator<User>>(), Microsoft.Extensions.Options.Options.Create(new OpenIddictOptions()),
            Microsoft.Extensions.Options.Options.Create(new GlobalOptions { AuthenticationSchemes = ["password"] }),
            Mock.Of<ISmsManager>(), Mock.Of<ILogger<AccountController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
        return (controller, users, signIn);
    }

    private sealed class PhoneTestDbContext(DbContextOptions<PhoneTestDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
    }
}
