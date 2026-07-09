namespace SamplePlugin.Models;

public sealed record GearsetInfo(
    int EnabledSlot,
    int InternalId,
    string Name,
    string JobName,
    int ItemLevel);
