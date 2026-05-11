using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Schedule;

public sealed class EventDay
{
    private EventDay() { } // EF

    [Key]
    public Guid EventDayId { get; private set; }

    public Guid EventId { get; private set; }

    public DateTime Date { get; private set; }

    [MaxLength(120)]
    public string Title { get; private set; } = null!;

    public int DayOrder { get; private set; }

    public List<Activity> Activities { get; private set; } = new();

    public EventDay(Guid eventId, DateTime date, string title, int dayOrder)
    {
        EventDayId = Guid.NewGuid();
        EventId = Guard.NotEmpty(eventId, nameof(eventId));
        Date = date.Date;
        Title = Guard.Required(title, nameof(Title), 120);
        DayOrder = Guard.NonNegative(dayOrder, nameof(dayOrder));
    }

    public void Rename(string title) => Title = Guard.Required(title, nameof(Title), 120);

    public void SetOrder(int order)
        => DayOrder = Guard.NonNegative(order, nameof(DayOrder));

    public void AddActivity(Activity activity)
    {
        Guard.Ensure(activity is not null, "Activity is required.");
        Activities.Add(activity);
    }

    public void RemoveActivity(Guid activityId)
    {
        var a = Activities.FirstOrDefault(x => x.ActivityId == activityId);
        Guard.Ensure(a is not null, "Activity not found.");
        Activities.Remove(a);
    }
}
