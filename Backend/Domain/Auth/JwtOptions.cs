namespace TripPlanner.Api.Features.Auth;

public sealed record JwtOptions(string Issuer, string Audience, string SigningKey, int AccessMinutes, int RefreshDays);
