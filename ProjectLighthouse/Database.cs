using System;
using System.Linq;
using System.Threading.Tasks;
using Kettu;
using LBPUnion.ProjectLighthouse.Helpers;
using LBPUnion.ProjectLighthouse.Logging;
using LBPUnion.ProjectLighthouse.Types;
using LBPUnion.ProjectLighthouse.Types.Levels;
using LBPUnion.ProjectLighthouse.Types.Profiles;
using LBPUnion.ProjectLighthouse.Types.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse
{
    public class Database : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Location> Locations { get; set; }
        public DbSet<Slot> Slots { get; set; }
        public DbSet<QueuedLevel> QueuedLevels { get; set; }
        public DbSet<HeartedLevel> HeartedLevels { get; set; }
        public DbSet<HeartedProfile> HeartedProfiles { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<GameToken> GameTokens { get; set; }
        public DbSet<WebToken> WebTokens { get; set; }
        public DbSet<Score> Scores { get; set; }
        public DbSet<PhotoSubject> PhotoSubjects { get; set; }
        public DbSet<Photo> Photos { get; set; }
        public DbSet<LastMatch> LastMatches { get; set; }
        public DbSet<VisitedLevel> VisitedLevels { get; set; }
        public DbSet<RatedLevel> RatedLevels { get; set; }
        public DbSet<AuthenticationAttempt> AuthenticationAttempts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseMySql(ServerSettings.Instance.DbConnectionString, MySqlServerVersion.LatestSupportedServerVersion);

        public async Task<User> CreateUser(string username, string password)
        {
            if (!password.StartsWith("$2a")) throw new ArgumentException(nameof(password) + " is not a BCrypt hash");

            User user;
            if ((user = await this.Users.Where(u => u.Username == username).FirstOrDefaultAsync()) != null) return user;

            Location l = new(); // store to get id after submitting
            this.Locations.Add(l); // add to table
            await this.SaveChangesAsync(); // saving to the database returns the id and sets it on this entity

            user = new User
            {
                Username = username,
                Password = password,
                LocationId = l.Id,
                Biography = username + " hasn't introduced themselves yet.",
            };
            this.Users.Add(user);

            await this.SaveChangesAsync();

            return user;
        }

        #nullable enable
        public async Task<GameToken?> AuthenticateUser(LoginData loginData, string userLocation, string titleId = "")
        {
            // TODO: don't use psn name to authenticate
            User? user = await this.Users.FirstOrDefaultAsync(u => u.Username == loginData.Username);
            if (user == null) return null;

            GameToken gameToken = new()
            {
                UserToken = HashHelper.GenerateAuthToken(),
                UserId = user.UserId,
                UserLocation = userLocation,
                GameVersion = GameVersionHelper.FromTitleId(titleId),
            };

            if (gameToken.GameVersion == GameVersion.Unknown)
            {
                Logger.Log($"Unknown GameVersion for TitleId {titleId}", LoggerLevelLogin.Instance);
                return null;
            }

            this.GameTokens.Add(gameToken);
            await this.SaveChangesAsync();

            return gameToken;
        }

        #region Game Token Shenanigans

        public async Task<User?> UserFromMMAuth(string authToken, bool allowUnapproved = false)
        {
            if (ServerStatics.IsUnitTesting) allowUnapproved = true;
            GameToken? token = await this.GameTokens.FirstOrDefaultAsync(t => t.UserToken == authToken);

            if (token == null) return null;
            if (!allowUnapproved && !token.Approved) return null;

            return await this.Users.Include(u => u.Location).FirstOrDefaultAsync(u => u.UserId == token.UserId);
        }

        public async Task<User?> UserFromGameToken
            (GameToken gameToken, bool allowUnapproved = false)
            => await this.UserFromMMAuth(gameToken.UserToken, allowUnapproved);

        public async Task<User?> UserFromGameRequest(HttpRequest request, bool allowUnapproved = false)
        {
            if (ServerStatics.IsUnitTesting) allowUnapproved = true;
            if (!request.Cookies.TryGetValue("MM_AUTH", out string? mmAuth) || mmAuth == null) return null;

            return await this.UserFromMMAuth(mmAuth, allowUnapproved);
        }

        public async Task<GameToken?> GameTokenFromRequest(HttpRequest request, bool allowUnapproved = false)
        {
            if (ServerStatics.IsUnitTesting) allowUnapproved = true;
            if (!request.Cookies.TryGetValue("MM_AUTH", out string? mmAuth) || mmAuth == null) return null;

            GameToken? token = await this.GameTokens.FirstOrDefaultAsync(t => t.UserToken == mmAuth);

            if (token == null) return null;
            if (!allowUnapproved && !token.Approved) return null;

            return token;
        }

        public async Task<(User, GameToken)?> UserAndGameTokenFromRequest(HttpRequest request, bool allowUnapproved = false)
        {
            if (ServerStatics.IsUnitTesting) allowUnapproved = true;
            if (!request.Cookies.TryGetValue("MM_AUTH", out string? mmAuth) || mmAuth == null) return null;

            GameToken? token = await this.GameTokens.FirstOrDefaultAsync(t => t.UserToken == mmAuth);
            if (token == null) return null;
            if (!allowUnapproved && !token.Approved) return null;

            User? user = await this.UserFromGameToken(token);

            if (user == null) return null;

            return (user, token);
        }

        #endregion

        #region Web Token Shenanigans

        public User? UserFromLighthouseToken(string lighthouseToken)
        {
            WebToken? token = this.WebTokens.FirstOrDefault(t => t.UserToken == lighthouseToken);
            if (token == null) return null;

            return this.Users.Include(u => u.Location).FirstOrDefault(u => u.UserId == token.UserId);
        }

        public User? UserFromWebRequest(HttpRequest request)
        {
            if (!request.Cookies.TryGetValue("LighthouseToken", out string? lighthouseToken) || lighthouseToken == null) return null;

            return this.UserFromLighthouseToken(lighthouseToken);
        }

        public WebToken? WebTokenFromRequest(HttpRequest request)
        {
            if (!request.Cookies.TryGetValue("LighthouseToken", out string? lighthouseToken) || lighthouseToken == null) return null;

            return this.WebTokens.FirstOrDefault(t => t.UserToken == lighthouseToken);
        }

        #endregion

        public async Task<Photo?> PhotoFromSubject(PhotoSubject subject)
            => await this.Photos.FirstOrDefaultAsync(p => p.PhotoSubjectIds.Contains(subject.PhotoSubjectId.ToString()));
        #nullable disable
    }
}