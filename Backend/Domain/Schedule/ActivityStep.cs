using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Schedule;

public sealed class ActivityStep
{
    private ActivityStep() { } // EF

    [Key]
    public Guid ActivityStepId { get; private set; }

    public Guid ActivityId { get; private set; }

    public int StepOrder { get; private set; }

    [MaxLength(1000)]
    public string Description { get; private set; } = null!;

    public bool IsMandatory { get; private set; }

    public ActivityStep(Guid activityId, int stepOrder, string description, bool isMandatory)
    {
        ActivityStepId = Guid.NewGuid();
        ActivityId = Guard.NotEmpty(activityId, nameof(activityId));
        StepOrder = Guard.NonNegative(stepOrder, nameof(stepOrder));
        Description = Guard.Required(description, nameof(Description), 1000);
        IsMandatory = isMandatory;
    }

    public void UpdateDescription(string text)
        => Description = Guard.Required(text, nameof(Description), 1000);

    public void MarkMandatory() => IsMandatory = true;
    public void MarkOptional() => IsMandatory = false;
}
