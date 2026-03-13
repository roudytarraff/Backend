using System.Text.RegularExpressions;

namespace TripPlanner.Api.Common;

public static class Guard
{
    public static void Ensure(bool condition, string message)
    {
        if (!condition) throw new DomainException(message);
    }

    public static Guid NotEmpty(Guid value, string field)
    {
        Ensure(value != Guid.Empty, $"{field} is required.");
        return value;
    }

    public static string Required(string? value, string field, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");

        var v = value.Trim();
        Ensure(v.Length <= maxLen, $"{field} must be <= {maxLen} characters.");
        return v;
    }

    public static string Email(string? value)
    {
        var v = Required(value, "Email", 254).ToLowerInvariant();
        Ensure(Regex.IsMatch(v, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"), "Invalid email format.");
        return v;
    }

    public static string? UrlOrNull(string? value, string field, int maxLen = 2048)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var v = value.Trim();
        Ensure(v.Length <= maxLen, $"{field} is too long.");
        Ensure(Uri.TryCreate(v, UriKind.Absolute, out _), $"{field} must be a valid absolute URL.");
        return v;
    }

    public static void DateRange(DateTime start, DateTime end, string startName, string endName)
        => Ensure(end >= start, $"{endName} must be after or equal to {startName}.");

    public static int NonNegative(int value, string field)
    {
        Ensure(value >= 0, $"{field} must be >= 0.");
        return value;
    }

    public static double NonNegative(double value, string field)
    {
        Ensure(value >= 0, $"{field} must be >= 0.");
        return value;
    }

    // ✅ NEW for Activity duration
    public static int EnsureDurationMinutes(int minutes)
    {
        Ensure(minutes > 0, "DurationMinutes must be > 0.");
        Ensure(minutes <= 24 * 60, "DurationMinutes is too large.");
        return minutes;
    }
}
