namespace PantheonAddonFramework.Models;

public sealed record ItemSnapshot(
    Guid InstanceId,
    int ItemId,
    string Name,
    int StackSize,
    string SlotType,
    int SlotIndex,
    long CharacterId,
    long CorpseId,
    string ParentGuid,
    string ModelName,
    IReadOnlyDictionary<string, string?> Instance,
    IReadOnlyDictionary<string, string?> Template,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> StatModifiers,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> InstanceStatModifiers,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> MultiplierModifiers,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> RequirementOverrides,
    IReadOnlyDictionary<string, object?> RawItemDump,
    IReadOnlyDictionary<string, object?> RawTemplateDump);
