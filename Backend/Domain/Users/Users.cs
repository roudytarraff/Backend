using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Users;

public sealed class User
{
    private User() { } // EF

    [Key]
    public Guid UserId { get; private set; }

    [MaxLength(60)]
    public string FirstName { get; private set; } = null!;

    [MaxLength(60)]
    public string LastName { get; private set; } = null!;

    [MaxLength(254)]
    public string Email { get; private set; } = null!;

    [MaxLength(2048)]
    public string? ProfilePictureUrl { get; private set; }

    public AccountStatus AccountStatus { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string? EmailVerificationCodeHash { get; set; }
    public DateTime? EmailVerificationExpiresAt { get; set; }
    public bool EmailVerified { get; set; }=false;

    [MaxLength(500)]
    public string PasswordHash { get; private set; } = null!;

    [MaxLength(500)]
    public string PasswordSalt { get; private set; } = null!;

    public List<RefreshToken> RefreshTokens { get; private set; } = new();

    public User(string firstName, string lastName, string email, string passwordHash, string passwordSalt)
    {
        UserId = Guid.NewGuid();

        FirstName = Guard.Required(firstName, nameof(FirstName), 60);
        LastName = Guard.Required(lastName, nameof(LastName), 60);
        Email = Guard.Email(email);

        PasswordHash = Guard.Required(passwordHash, nameof(PasswordHash), 500);
        PasswordSalt = Guard.Required(passwordSalt, nameof(PasswordSalt), 500);

        AccountStatus = AccountStatus.Active;
        CreatedAt = DateTime.UtcNow;
    }

    public void Activate() => AccountStatus = AccountStatus.Active;
    public void Deactivate() => AccountStatus = AccountStatus.Deactivated;

    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = Guard.Required(firstName, nameof(FirstName), 60);
        LastName = Guard.Required(lastName, nameof(LastName), 60);
    }

    public void UpdateProfilePicture(string? url)
        => ProfilePictureUrl = Guard.UrlOrNull(url, nameof(ProfilePictureUrl), 2048);

    public RefreshToken IssueRefreshToken(string tokenHash, DateTime expiresAtUtc)
    {
        Guard.Ensure(expiresAtUtc > DateTime.UtcNow, "Refresh token expiry must be in the future.");
        var rt = new RefreshToken(UserId, tokenHash, expiresAtUtc);
        RefreshTokens.Add(rt);
        return rt;
    }
}