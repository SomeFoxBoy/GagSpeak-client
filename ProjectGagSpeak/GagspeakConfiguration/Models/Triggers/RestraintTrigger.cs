namespace GagSpeak.GagspeakConfiguration.Models;

/// <summary>
/// Trigger fired when a restraint set goes into a specific state.
/// </summary>
[Serializable]
public record RestraintTrigger : Trigger
{
    public override TriggerKind Type => TriggerKind.RestraintSet;

    // the kind of restraint set that will invoke this trigger's execution
    public Guid RestraintSetId { get; set; } = Guid.Empty;

    // the new state of it that will trigger the execution
    public NewState RestraintState { get; set; } = NewState.Enabled;

    public override RestraintTrigger DeepClone()
    {
        return new RestraintTrigger
        {
            Identifier = Identifier,
            Enabled = Enabled,
            Priority = Priority,
            Name = Name,
            Description = Description,
            ExecutableAction = ExecutableAction.DeepClone(),
            RestraintSetId = RestraintSetId,
            RestraintState = RestraintState
        };
    }
}
