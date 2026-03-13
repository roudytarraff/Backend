namespace TripPlanner.Api.Common;

public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message) { }
}
