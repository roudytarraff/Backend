using System;

namespace Backend.Controllers.EventCreation;

using System.ComponentModel.DataAnnotations;

public sealed class CreateEventRequest
{
    [Required]
    [MaxLength(120)]
    public string Title { get; set; } = "";

    [Required]
    [MaxLength(4000)]
    public string Description { get; set; } = "";

    [Required]
    [MaxLength(60)]
    public string EventType { get; set; } = "Trip";

    public DateTime StartDate { get; set; }

    public TimeSpan StartTime { get; set; }

    // Trip destination
    public string DestinationName { get; set; } = "";

    public double DestinationLatitude { get; set; }

    public double DestinationLongitude { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string JoinPassword { get; set; } = "";

    public List<EventDayRequest> Days { get; set; } = new();
}


public sealed class EventDayRequest
{
    public DateTime Date { get; set; }

    public string Title { get; set; } = "";

    public List<ActivityRequest> Activities { get; set; } = new();
}


public sealed class ActivityRequest
{
    public string Title { get; set; } = "";

    public string Type { get; set; } = "";

    public int DurationMinutes { get; set; }

    public int Order { get; set; }

    public string LocationName { get; set; } = "";

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? ThumbnailUrl { get; set; }

    public List<ActivityStepRequest> Steps { get; set; } = new();
}


public sealed class ActivityStepRequest
{
    public int StepOrder { get; set; }

    public string Description { get; set; } = "";

    public bool IsMandatory { get; set; }
}
