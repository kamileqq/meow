#nullable enable
using JetBrains.Annotations;
using LBPUnion.ProjectLighthouse.Configuration;
using LBPUnion.ProjectLighthouse.Extensions;
using LBPUnion.ProjectLighthouse.Helpers;
using LBPUnion.ProjectLighthouse.Logging;
using LBPUnion.ProjectLighthouse.PlayerData;
using LBPUnion.ProjectLighthouse.PlayerData.Profiles;
using LBPUnion.ProjectLighthouse.PlayerData.Profiles.Email;
using LBPUnion.ProjectLighthouse.Servers.Website.Pages.Layouts;
using LBPUnion.ProjectLighthouse.Types;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Servers.Website.Pages;

public class LoginForm : BaseLayout
{
    public LoginForm(Database database) : base(database)
    {}

    public string? Error { get; private set; }

    [UsedImplicitly]
    public async Task<IActionResult> OnPost(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            this.Error = "The username field is required.";
            return this.Page();
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            this.Error = "The password field is required.";
            return this.Page();
        }

        if (!await this.Request.CheckCaptchaValidity())
        {
            this.Error = "You must complete the captcha correctly.";
            return this.Page();
        }

        User? user = await this.Database.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null)
        {
            Logger.Warn($"User {username} failed to login on web due to invalid username", LogArea.Login);
            this.Error = "The username or password you entered is invalid.";
            return this.Page();
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.Password))
        {
            Logger.Warn($"User {user.Username} (id: {user.UserId}) failed to login on web due to invalid password", LogArea.Login);
            this.Error = "The username or password you entered is invalid.";
            return this.Page();
        }

        if (user.Banned)
        {
            Logger.Warn($"User {user.Username} (id: {user.UserId}) failed to login on web due to being banned", LogArea.Login);
            this.Error = "You have been banned. Please contact an administrator for more information.\nReason: " + user.BannedReason;
            return this.Page();
        }

        if (user.EmailAddress == null && ServerConfiguration.Instance.Mail.MailEnabled)
        {
            Logger.Warn($"User {user.Username} (id: {user.UserId}) failed to login; email not set", LogArea.Login);

            EmailSetToken emailSetToken = new()
            {
                UserId = user.UserId,
                User = user,
                EmailToken = CryptoHelper.GenerateAuthToken(),
            };

            this.Database.EmailSetTokens.Add(emailSetToken);
            await this.Database.SaveChangesAsync();

            return this.Redirect("/login/setEmail?token=" + emailSetToken.EmailToken);
        }

        WebToken webToken = new()
        {
            UserId = user.UserId,
            UserToken = CryptoHelper.GenerateAuthToken(),
        };

        this.Database.WebTokens.Add(webToken);
        await this.Database.SaveChangesAsync();

        this.Response.Cookies.Append
        (
            "LighthouseToken",
            webToken.UserToken,
            new CookieOptions
            {
                Expires = DateTimeOffset.Now.AddDays(7),
            }
        );

        Logger.Success($"User {user.Username} (id: {user.UserId}) successfully logged in on web", LogArea.Login);

        if (user.PasswordResetRequired) return this.Redirect("~/passwordResetRequired");
        if (ServerConfiguration.Instance.Mail.MailEnabled && !user.EmailAddressVerified) return this.Redirect("~/login/sendVerificationEmail");

        return this.RedirectToPage(nameof(LandingPage));
    }

    [UsedImplicitly]
    public IActionResult OnGet() => this.Page();
}