using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Users;

public sealed class RefreshToken
{
    private RefreshToken() { } // EF

    [Key]
    public Guid RefreshTokenId { get; private set; }

    public Guid UserId { get; private set; }

    [MaxLength(2000)]
    public string TokenHash { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public RefreshTokenStatus Status { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public User User { get; private set; } = null!;

    public RefreshToken(Guid userId, string tokenHash, DateTime expiresAtUtc)
    {
        RefreshTokenId = Guid.NewGuid();
        UserId = Guard.NotEmpty(userId, nameof(UserId));
        TokenHash = Guard.Required(tokenHash, nameof(TokenHash), 2000);

        CreatedAt = DateTime.UtcNow;
        ExpiresAt = expiresAtUtc;
        Status = RefreshTokenStatus.Active;
    }

    public bool IsValid(DateTime nowUtc)
        => Status == RefreshTokenStatus.Active && RevokedAt is null && ExpiresAt > nowUtc;

    public void Revoke()
    {
        if (Status == RefreshTokenStatus.Revoked) return;
        Status = RefreshTokenStatus.Revoked;
        RevokedAt = DateTime.UtcNow;
    }

    public void MarkExpired()
    {
        if (Status == RefreshTokenStatus.Expired) return;
        Status = RefreshTokenStatus.Expired;
    }

    public void LinkReplacement(Guid newTokenId)
        => ReplacedByTokenId = Guard.NotEmpty(newTokenId, nameof(ReplacedByTokenId));
}
