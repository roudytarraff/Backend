using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using TripPlanner.Api.Common;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Users;

namespace TripPlanner.Api.Features.Auth;

public sealed class AuthModule
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwt;
    private readonly IConfiguration _config;

    public AuthModule(AppDbContext db, JwtOptions jwt, IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _config = config;
    }

    public async Task<AuthResponse> Register(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var existingUser = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (existingUser != null)
        {
            if (existingUser.EmailVerified)
                throw new DomainException("Email already registered.");

            // delete unverified user
            _db.Users.Remove(existingUser);
        }

        var (hash, salt) = HashPassword(req.Password);

        var user = new User(req.FirstName, req.LastName, email, hash, salt);
        _db.Users.Add(user);

        // ---------- OTP ----------
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var otpHash = Sha256(otp);

        user.EmailVerificationCodeHash = otpHash;
        user.EmailVerificationExpiresAt = DateTime.UtcNow.AddMinutes(1);

        await SendOtpEmail(email, otp, ct);
        // -------------------------

        var access = CreateAccessToken(user);

        var refreshPlain = GenerateSecureToken();
        var refreshHash = Sha256(refreshPlain);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshDays);

        user.IssueRefreshToken(refreshHash, refreshExpires);

        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            UserId = user.UserId,
            AccessToken = access.Token,
            RefreshToken = refreshPlain,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshExpires
        };
    }

    public async Task<AuthResponse> Login(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        Guard.Ensure(user is not null, "Incorrect Email or Password.");
        Guard.Ensure(user.AccountStatus == Domain.AccountStatus.Active, "Account is not active.");
        Guard.Ensure(VerifyPassword(req.Password, user.PasswordHash, user.PasswordSalt), "Invalid credentials.");

        if (!user.EmailVerified)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            var otpHash = Sha256(otp);

            user.EmailVerificationCodeHash = otpHash;
            user.EmailVerificationExpiresAt = DateTime.UtcNow.AddMinutes(1);

            await SendOtpEmail(user.Email, otp, ct);

            await _db.SaveChangesAsync(ct);

            throw new DomainException("Email not verified. OTP sent.");
        }

        var access = CreateAccessToken(user);

        var refreshPlain = GenerateSecureToken();
        var refreshHash = Sha256(refreshPlain);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshDays);

        user.IssueRefreshToken(refreshHash, refreshExpires);

        const int maxActiveTokens = 5;

        var active = user.RefreshTokens
            .Where(t => t.Status == RefreshTokenStatus.Active && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        foreach (var old in active.Skip(maxActiveTokens))
            old.Revoke();

        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            UserId = user.UserId,
            AccessToken = access.Token,
            RefreshToken = refreshPlain,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshExpires
        };
    }

    public async Task<AuthResponse> VerifyEmailOtp(VerifyEmailRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        Guard.Ensure(user is not null, "User not found.");
        Guard.Ensure(user.EmailVerificationCodeHash != null, "OTP not generated.");

        Guard.Ensure(
            user.EmailVerificationExpiresAt > DateTime.UtcNow,
            "OTP expired."
        );

        var hash = Sha256(req.Otp);

        Guard.Ensure(hash == user.EmailVerificationCodeHash, "Invalid OTP.");

        user.EmailVerified = true;
        user.EmailVerificationCodeHash = null;
        user.EmailVerificationExpiresAt = null;

        var access = CreateAccessToken(user);

        var refreshPlain = GenerateSecureToken();
        var refreshHash = Sha256(refreshPlain);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshDays);

        user.IssueRefreshToken(refreshHash, refreshExpires);

        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            UserId = user.UserId,
            AccessToken = access.Token,
            RefreshToken = refreshPlain,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshExpires
        };
    }

    public async Task<AuthResponse> Refresh(RefreshRequest req, CancellationToken ct)
    {
        Guard.Ensure(!string.IsNullOrWhiteSpace(req.RefreshToken), "Refresh token required.");

        var presentedHash = Sha256(req.RefreshToken);

        var tokenRow = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == presentedHash, ct);

        if (tokenRow is null)
            throw new UnauthorizedException("Invalid refresh token.");

        if (!tokenRow.IsValid(DateTime.UtcNow))
        {
            tokenRow.MarkExpired();
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedException("Refresh token expired or revoked.");
        }

        var user = tokenRow.User;

        if (user.AccountStatus != Domain.AccountStatus.Active)
            throw new UnauthorizedException("Account is not active.");

        tokenRow.Revoke();

        var newRefreshPlain = GenerateSecureToken();
        var newRefreshHash = Sha256(newRefreshPlain);
        var newRefreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshDays);

        var newRt = user.IssueRefreshToken(newRefreshHash, newRefreshExpires);
        tokenRow.LinkReplacement(newRt.RefreshTokenId);

        var access = CreateAccessToken(user);

        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            UserId = user.UserId,
            AccessToken = access.Token,
            RefreshToken = newRefreshPlain,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = newRefreshExpires
        };
    }

    public async Task Logout(RefreshRequest req, CancellationToken ct)
    {
        Guard.Ensure(!string.IsNullOrWhiteSpace(req.RefreshToken), "Refresh token required.");

        var presentedHash = Sha256(req.RefreshToken);
        var tokenRow = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == presentedHash, ct);
        if (tokenRow is null) return;

        tokenRow.Revoke();
        await _db.SaveChangesAsync(ct);
    }

    // -------- EMAIL FUNCTION --------
    private async Task SendOtpEmail(string email, string otp, CancellationToken ct)
    {
        var host = _config["Email:Host"];
        var port = int.Parse(_config["Email:Port"]!);
        var username = _config["Email:Username"];
        var password = _config["Email:Password"];
        var from = _config["Email:From"];

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "TripPlanner Email Verification";

        message.Body = new TextPart("plain")
        {
            Text = $"Your verification code is: {otp}\n\nThis code expires in 1 minutes."
        };

        using var client = new SmtpClient();

        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(username, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }


    public async Task ResendOtp(ResendOtpRequest req, CancellationToken ct)
{
    var email = req.Email.Trim().ToLowerInvariant();

    var user = await _db.Users
        .FirstOrDefaultAsync(u => u.Email == email, ct);

    Guard.Ensure(user is not null, "User not found.");
    Guard.Ensure(!user.EmailVerified, "Email already verified.");

    // generate new OTP
    var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
    var otpHash = Sha256(otp);

    user.EmailVerificationCodeHash = otpHash;
    user.EmailVerificationExpiresAt = DateTime.UtcNow.AddMinutes(10);

    await SendOtpEmail(email, otp, ct);

    await _db.SaveChangesAsync(ct);
}

    // -------- JWT --------
    private (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_jwt.AccessMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("uid", user.UserId.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return (jwt, expires);
    }

    // -------- PASSWORD HASH --------
    private static (string Hash, string Salt) HashPassword(string password)
    {
        Guard.Ensure(!string.IsNullOrWhiteSpace(password), "Password is required.");

        byte[] salt = RandomNumberGenerator.GetBytes(16);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100000,
            HashAlgorithmName.SHA256,
            32);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    private static bool VerifyPassword(string password, string storedHashB64, string storedSaltB64)
    {
        var salt = Convert.FromBase64String(storedSaltB64);

        var computed = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100000,
            HashAlgorithmName.SHA256,
            32);

        var storedHash = Convert.FromBase64String(storedHashB64);

        return CryptographicOperations.FixedTimeEquals(computed, storedHash);
    }

    // -------- TOKEN HELPERS --------
    private static string GenerateSecureToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string Sha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}