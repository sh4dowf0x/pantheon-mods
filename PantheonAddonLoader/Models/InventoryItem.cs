using Il2Cpp;
using PantheonAddonFramework.Models;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PantheonAddonLoader.Models;

internal class InventoryItem : IInventoryItem
{
    private readonly Item _item;
    
    public InventoryItem(Item item)
    {
        _item = item;
    }

    public Guid Id => SafeGuid(() => _item.ItemInstanceGuid.ToString());
    public string Name => SafeString(() => _item.Template.ItemName) ?? "";

    public ItemSnapshot GetSnapshot(bool includeRawDump = false)
    {
        var template = _item.Template;
        var equipSlot = ResolveEquipSlot(_item, template);
        var classRequirement = ResolveClassRequirement(template);
        var instanceFields = new Dictionary<string, string?>
        {
            ["persistedItemFlags"] = SafeString(() => _item.PersistedItemFlags),
            ["trackedByQuestId"] = SafeString(() => _item.TrackedByQuestId),
            ["isStackable"] = SafeString(() => _item.IsStackable()),
            ["isSplittable"] = SafeString(() => _item.IsSplittable()),
            ["isCurrency"] = SafeString(() => _item.IsCurrency()),
            ["isBag"] = SafeString(() => _item.IsBag()),
            ["isEquippedBag"] = SafeString(() => _item.IsEquippedBag()),
            ["isHeldItem"] = SafeString(() => _item.IsHeldItem()),
            ["isWeapon"] = SafeString(() => _item.IsWeapon()),
            ["isShield"] = SafeString(() => _item.IsShield()),
            ["isGatheringTool"] = SafeString(() => _item.IsGatheringTool()),
            ["isDeathbound"] = SafeString(() => _item.IsDeathbound()),
            ["isLifebound"] = SafeString(() => _item.IsLifebound()),
            ["canBeTraded"] = SafeString(() => _item.CanBeTraded()),
            ["clientCanBeSold"] = SafeString(() => _item.ClientCanBeSold()),
            ["shieldDamageReductionPercent"] = SafeString(() => _item.GetShieldDamageReductionPercent()),
            ["availableSpaceOnStack"] = SafeString(() => _item.GetAvailableSpaceOnStack()),
            ["equipSlotName"] = equipSlot.Name,
            ["equipSlotSource"] = equipSlot.Source
        };

        var templateFields = new Dictionary<string, string?>
        {
            ["itemId"] = SafeString(() => template.ItemId),
            ["itemKey"] = SafeString(() => template.ItemKey),
            ["itemName"] = SafeString(() => template.ItemName),
            ["itemDescription"] = SafeString(() => template.ItemDescription),
            ["iconKey"] = SafeString(() => template.IconKey),
            ["itemType"] = SafeString(() => template.ItemTypeId),
            ["containerType"] = SafeString(() => template.ContainerType),
            ["designerNotes"] = SafeString(() => template.DesignerNotes),
            ["itemFlags"] = SafeString(() => template.ItemFlags),
            ["itemWeight"] = SafeString(() => template.ItemWeight),
            ["useSeconds"] = SafeString(() => template.UseSeconds),
            ["useRestrictions"] = SafeString(() => template.UseRestrictions),
            ["useAnimation"] = SafeString(() => template.UseAnimation),
            ["itemLevel"] = SafeString(() => template.ItemLevel),
            ["requiredLevel"] = SafeString(() => template.RequiredLevel),
            ["armorType"] = SafeString(() => template.ArmorType),
            ["armorTypeName"] = SafeString(() => template.GetArmorType()),
            ["toolType"] = SafeString(() => template.ToolType),
            ["damageType"] = SafeString(() => template.DamageType),
            ["damageTypeName"] = SafeString(() => template.GetDamageType()),
            ["allowedLocations"] = SafeString(() => template.AllowedLocations),
            ["equipSlotName"] = equipSlot.Name,
            ["equipSlotSource"] = equipSlot.Source,
            ["allowedRaces"] = SafeString(() => template.AllowedRaces),
            ["allowedClasses"] = SafeString(() => template.AllowedClasses),
            ["classRequirementNames"] = classRequirement.Classes,
            ["classRequirementSource"] = classRequirement.Source,
            ["requiredProficiency"] = classRequirement.Proficiency,
            ["classSetId"] = SafeString(() => template.ClassSetId),
            ["maxStackSizeOrCharges"] = SafeString(() => template.MaxStackSizeOrCharges),
            ["maxStackSize"] = SafeString(() => template.MaxStackSize),
            ["containerCapacity"] = SafeString(() => template.ContainerCapacity),
            ["maxDamage"] = SafeString(() => template.MaxDamage),
            ["delay"] = SafeString(() => template.Delay),
            ["coinValue"] = SafeString(() => template.CoinValue),
            ["buyPrice"] = SafeString(() => template.BuyPrice),
            ["buyPriceOrDefault"] = SafeString(() => template.BuyPriceOrDefault),
            ["primarySkill"] = SafeString(() => template.PrimarySkill),
            ["skillEffectiveness"] = SafeString(() => template.SkillEffectiveness),
            ["rarity"] = SafeString(() => template.RarityId),
            ["requiredQuestId"] = SafeString(() => template.RequiredQuestId),
            ["questIdToStart"] = SafeString(() => template.GetQuestIdToStart()),
            ["weaponType"] = SafeString(() => template.WeaponType),
            ["modelId"] = SafeString(() => template.ModelId),
            ["inWorldModelId"] = SafeString(() => template.InWorldModelId),
            ["activatedAbilityId"] = SafeString(() => template.ActivatedAbilityId),
            ["activatedBuffId"] = SafeString(() => template.ActivatedBuffId),
            ["equipBuffId"] = SafeString(() => template.EquipBuffId),
            ["learnedAbilityId"] = SafeString(() => template.LearnedAbilityId),
            ["polarity"] = SafeString(() => template.Polarity),
            ["duration"] = SafeString(() => template.Duration),
            ["potency"] = SafeString(() => template.Potency),
            ["primaryBonus"] = SafeString(() => template.PrimaryBonus),
            ["secondaryBonus"] = SafeString(() => template.SecondaryBonus),
            ["effectId"] = SafeString(() => template.EffectId),
            ["damageModifier"] = SafeString(() => template.DamageModifier),
            ["delayModifier"] = SafeString(() => template.DelayModifier),
            ["durabilityModifier"] = SafeString(() => template.DurabilityModifier),
            ["armorModifier"] = SafeString(() => template.ArmorModifier),
            ["effectivenessMod"] = SafeString(() => template.EffectivenessMod),
            ["blockMod"] = SafeString(() => template.BlockMod),
            ["blockValueMod"] = SafeString(() => template.BlockValueMod),
            ["recipeId"] = SafeString(() => template.RecipeId),
            ["craftingFamily"] = SafeString(() => template.CraftingFamily),
            ["durability"] = SafeString(() => template.Durability),
            ["itemMaterialTypeId"] = SafeString(() => template.ItemMaterialTypeId),
            ["canBeSold"] = SafeString(() => template.CanBeSold()),
            ["canBeTraded"] = SafeString(() => template.CanBeTraded()),
            ["isWeapon"] = SafeString(() => template.IsWeapon()),
            ["isGatheringTool"] = SafeString(() => template.IsGatheringTool()),
            ["isBag"] = SafeString(() => template.IsBag()),
            ["isStackable"] = SafeString(() => template.IsStackable()),
            ["isCurrency"] = SafeString(() => template.IsCurrency())
        };

        var statModifiers = SerializeModifierEntries(template.StatModifiers, "statModifier");
        var computedStatModifiers = SerializeComputedItemStats(_item);
        var multiplierModifiers = SerializeModifierEntries(template.MultiplierModifiers, "multiplierModifier");
        var requirementOverrides = SerializeGenericEntries(template.RequirementOverrides);
        var rawItemDump = includeRawDump ? SerializeObjectGraph(_item) : new Dictionary<string, object?>();
        var rawTemplateDump = includeRawDump ? SerializeObjectGraph(template) : new Dictionary<string, object?>();

        return new ItemSnapshot(
            InstanceId: Id,
            ItemId: SafeInt(() => _item.ItemId),
            Name: Name,
            StackSize: SafeInt(() => _item.StackSize),
            SlotType: SafeString(() => _item.SlotType) ?? "",
            SlotIndex: SafeInt(() => _item.SlotIndex),
            CharacterId: SafeLong(() => _item.CharacterId),
            CorpseId: SafeLong(() => _item.CorpseID),
            ParentGuid: SafeString(() => _item.ParentGuid) ?? "",
            ModelName: SafeString(() => _item.ModelName) ?? "",
            Instance: instanceFields,
            Template: templateFields,
            StatModifiers: statModifiers,
            InstanceStatModifiers: computedStatModifiers,
            MultiplierModifiers: multiplierModifiers,
            RequirementOverrides: requirementOverrides,
            RawItemDump: rawItemDump,
            RawTemplateDump: rawTemplateDump);
    }

    private static Guid SafeGuid(Func<object?> read)
    {
        try
        {
            var value = read()?.ToString();
            return Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;
        }
        catch
        {
            return Guid.Empty;
        }
    }

    private static int SafeInt(Func<object?> read)
    {
        try
        {
            var value = read();
            if (value is int typed)
            {
                return typed;
            }

            return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long SafeLong(Func<object?> read)
    {
        try
        {
            var value = read();
            if (value is long typed)
            {
                return typed;
            }

            return long.TryParse(value?.ToString(), out var parsed) ? parsed : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private static string? SafeString(Func<object?> read)
    {
        try
        {
            return SafeToString(read());
        }
        catch
        {
            return null;
        }
    }

    private sealed record ResolvedMetadata(string? Name, string? Source);

    private sealed record ClassRequirementMetadata(string? Classes, string? Source, string? Proficiency);

    private static ResolvedMetadata ResolveEquipSlot(Item item, ItemTemplate template)
    {
        var slotType = SafeString(() => item.SlotType);
        if (string.Equals(slotType, "Equipped", StringComparison.OrdinalIgnoreCase))
        {
            var currentSlot = EquipSlotNameFromIndex(SafeInt(() => item.SlotIndex));
            if (!string.IsNullOrWhiteSpace(currentSlot))
            {
                return new ResolvedMetadata(currentSlot, "currentEquippedSlot");
            }
        }

        var inferred = InferEquipSlotName(template);
        return string.IsNullOrWhiteSpace(inferred)
            ? new ResolvedMetadata(null, null)
            : new ResolvedMetadata(inferred, "inferredFromItemTypeAndName");
    }

    private static string? EquipSlotNameFromIndex(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= (int)EquipSlotType.Max)
        {
            return null;
        }

        try
        {
            return EquipSlotTypeExtensions.AsString((EquipSlotType)slotIndex);
        }
        catch
        {
            return ((EquipSlotType)slotIndex).ToString();
        }
    }

    private static string? InferEquipSlotName(ItemTemplate template)
    {
        var itemType = SafeString(() => template.ItemTypeId);
        if (string.Equals(itemType, "Weapon", StringComparison.OrdinalIgnoreCase))
        {
            return "Primary Hand";
        }

        if (string.Equals(itemType, "Shield", StringComparison.OrdinalIgnoreCase))
        {
            return "Secondary Hand";
        }

        if (string.Equals(itemType, "Held", StringComparison.OrdinalIgnoreCase))
        {
            return "Primary Hand";
        }

        if (!string.Equals(itemType, "Armor", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = SafeString(() => template.ItemName) ?? "";
        return name switch
        {
            var value when ContainsAny(value, "cap", "cowl", "helm", "hood", "skullcap") => "Head",
            var value when ContainsAny(value, "bracer", "wristband") => "Wrist",
            var value when ContainsAny(value, "jerkin", "robe", "garb", "chestwrap") => "Chest",
            var value when ContainsAny(value, "cloak") => "Back",
            var value when ContainsAny(value, "glove") => "Hands",
            var value when ContainsAny(value, "belt", "sash") => "Waist",
            var value when ContainsAny(value, "legging", "pants") => "Legs",
            var value when ContainsAny(value, "boot", "sandal") => "Feet",
            var value when ContainsAny(value, "necklace", "amulet", "choker") => "Neck",
            _ => null
        };
    }

    private static ClassRequirementMetadata ResolveClassRequirement(ItemTemplate template)
    {
        var proficiency = ResolveRequiredProficiency(template);
        if (string.IsNullOrWhiteSpace(proficiency))
        {
            return new ClassRequirementMetadata(null, null, null);
        }

        var classes = ClassNamesForProficiency(proficiency);
        return classes.Length > 0
            ? new ClassRequirementMetadata(string.Join(", ", classes), "inferredFromRequiredProficiency", proficiency)
            : new ClassRequirementMetadata(null, null, proficiency);
    }

    private static string? ResolveRequiredProficiency(ItemTemplate template)
    {
        var itemType = SafeString(() => template.ItemTypeId);
        if (string.Equals(itemType, "Armor", StringComparison.OrdinalIgnoreCase))
        {
            return SafeString(() => template.GetArmorType()) switch
            {
                "Cloth" => "Cloth",
                "MartialCloth" => "Martial Cloth",
                "LightLeather" => "Leather",
                "HeavyLeather" => "Leather",
                "LightChain" => "Chain and Scale",
                "HeavyChain" => "Chain and Scale",
                "LightPlate" => "Light Plate",
                "HeavyPlate" => "Heavy Plate",
                _ => null
            };
        }

        var primarySkill = SafeString(() => template.PrimarySkill);
        return string.Equals(primarySkill, "None", StringComparison.OrdinalIgnoreCase) ? null : primarySkill;
    }

    private static string[] ClassNamesForProficiency(string proficiency)
    {
        return proficiency switch
        {
            "Leather" => new[] { "Warrior", "Rogue", "Cleric", "Paladin", "Dire Lord", "Ranger", "Druid", "Shaman", "Bard" },
            "Martial Cloth" => new[] { "Monk" },
            "Cloth" => new[] { "Warrior", "Monk", "Rogue", "Cleric", "Paladin", "Wizard", "Dire Lord", "Ranger", "Druid", "Enchanter", "Shaman", "Summoner", "Bard" },
            "Chain and Scale" => new[] { "Warrior", "Cleric", "Paladin", "Dire Lord", "Ranger", "Shaman", "Bard" },
            "Light Plate" => new[] { "Warrior", "Cleric", "Paladin", "Dire Lord" },
            "Heavy Plate" => new[] { "Warrior", "Paladin", "Dire Lord" },
            "Swords" => new[] { "Warrior", "Rogue", "Paladin", "Dire Lord", "Ranger", "Bard" },
            "GreatAxes" => new[] { "Warrior", "Paladin", "Dire Lord" },
            "GreatSwords" => new[] { "Warrior", "Paladin", "Dire Lord" },
            "Daggers" => new[] { "Rogue", "Ranger", "Bard" },
            "Hammers" => new[] { "Warrior", "Cleric", "Paladin", "Dire Lord", "Shaman" },
            "GreatHammers" => new[] { "Warrior", "Cleric", "Paladin", "Dire Lord" },
            "MartialStaves" => new[] { "Monk", "Druid", "Shaman" },
            "Shields" => new[] { "Warrior", "Cleric", "Paladin", "Dire Lord", "Ranger", "Shaman", "Bard" },
            "ShortSpears" => new[] { "Warrior", "Rogue", "Ranger", "Shaman", "Bard" },
            "LongSpears" => new[] { "Warrior", "Ranger", "Shaman" },
            _ => Array.Empty<string>()
        };
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string? SafeEnumerable(Func<System.Collections.IEnumerable?> read, bool describeItems = false)
    {
        try
        {
            var value = read();
            if (value == null)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var item in value)
            {
                parts.Add(describeItems ? DescribeObjectText(item) : (item?.ToString() ?? ""));
            }

            return string.Join("; ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> SerializeModifierEntries(System.Collections.IEnumerable? values, string fallbackType)
    {
        var entries = new List<IReadOnlyDictionary<string, string?>>();
        if (values == null)
        {
            return entries;
        }

        foreach (var item in values)
        {
            var details = DescribeModifierEntry(item);
            if (details.Count == 0)
            {
                continue;
            }

            var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = details.TryGetValue("type", out var type) ? type : fallbackType
            };

            foreach (var detail in details)
            {
                normalized[NormalizeModifierKey(detail.Key)] = detail.Value;
            }

            entries.Add(normalized);
        }

        return entries;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> SerializeComputedItemStats(Item item)
    {
        var entries = new List<IReadOnlyDictionary<string, string?>>();

        foreach (var statType in EnumerateStatTypes())
        {
            try
            {
                var value = item.GetStatValue(statType);
                if (Math.Abs(value) < 0.0001f)
                {
                    continue;
                }

                entries.Add(new Dictionary<string, string?>
                {
                    ["type"] = "computedItemStat",
                    ["stat"] = SafeToString(statType),
                    ["modifierValue"] = value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    ["source"] = "Item.GetStatValue"
                });
            }
            catch
            {
            }
        }

        return entries;
    }

    private static IEnumerable<StatType> EnumerateStatTypes()
    {
        foreach (var field in typeof(StatType).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            object? value;
            try
            {
                value = field.GetValue(null);
            }
            catch
            {
                continue;
            }

            if (value is StatType statType)
            {
                yield return statType;
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> DescribeModifierEntry(object? value)
    {
        if (value == null)
        {
            return new Dictionary<string, string?>();
        }

        var tupleDetails = DescribeStatModifierTuple(value);
        return tupleDetails.Count > 0 ? tupleDetails : DescribeObject(value);
    }

    private static IReadOnlyDictionary<string, string?> DescribeStatModifierTuple(object value)
    {
        object? stat = null;
        object? modifier = null;

        try
        {
            if (value is ITuple tuple && tuple.Length >= 2)
            {
                stat = tuple[0];
                modifier = tuple[1];
            }
        }
        catch
        {
        }

        stat ??= ReadMemberValue(value, "Item1");
        modifier ??= ReadMemberValue(value, "Item2");
        var fieldGetterStat = ReadIl2CppField(value, "Item1");
        var fieldGetterModifier = ReadIl2CppField(value, "Item2");
        stat = fieldGetterStat.Value ?? stat;
        modifier = fieldGetterModifier.Value ?? modifier;
        var tupleStat = stat;
        var attachedStatType = ReadAttachedStatType(modifier);

        if (stat == null && modifier == null)
        {
            return new Dictionary<string, string?>();
        }

        var result = new Dictionary<string, string?>
        {
            ["type"] = "instanceStatModifier",
            ["stat"] = SafeToString(stat)
        };

        AddDebugValue(result, "tupleTypeName", value.GetType().FullName ?? value.GetType().Name);
        AddDebugValue(result, "tupleMemberNames", DescribeMemberNames(value));
        AddDebugValue(result, "tupleMethodSignatures", DescribeMethodSignatures(value, 80));
        AddDebugValue(result, "tupleMembersSample", DescribeObjectMembers(value, 20));
        AddDebugValue(result, "tupleFieldGetterItem1", fieldGetterStat.Value);
        AddDebugValue(result, "tupleFieldGetterItem1Underlying", ReadEnumUnderlyingValue(fieldGetterStat.Value));
        AddDebugValue(result, "tupleFieldGetterItem1Trace", fieldGetterStat.Trace);
        AddDebugValue(result, "tupleFieldGetterItem2", fieldGetterModifier.Value);
        AddDebugValue(result, "tupleFieldGetterItem2Trace", fieldGetterModifier.Trace);
        AddDebugValue(result, "tupleStat", tupleStat);
        AddDebugValue(result, "tupleStatUnderlying", ReadEnumUnderlyingValue(tupleStat));
        AddDebugValue(result, "tupleStatTypeName", tupleStat?.GetType().FullName ?? tupleStat?.GetType().Name);
        AddDebugValue(result, "tupleStatMemberNames", DescribeMemberNames(tupleStat));
        AddDebugValue(result, "tupleStatMembersSample", DescribeObjectMembers(tupleStat, 20));
        AddDebugValue(result, "attachedStatType", attachedStatType);
        AddDebugValue(result, "attachedStatTypeUnderlying", ReadEnumUnderlyingValue(attachedStatType));

        AddMember(result, modifier, "modifierValue", "Value");
        AddMember(result, modifier, "modifierType", "ModifierType");
        AddMember(result, modifier, "modifierCategory", "ModifierCategory");
        AddMember(result, modifier, "priority", "Priority");
        AddMember(result, modifier, "applyToBaseValue", "ApplyToBaseValue");
        AddKnownMemberDebug(result, modifier, "modifier");
        AddAttachedStatsDebug(result, modifier);

        if (!result.ContainsKey("modifierValue"))
        {
            result["modifier"] = SafeToString(modifier);
        }

        return result;
    }

    private static object? ReadAttachedStatType(object? modifier)
    {
        var stats = ReadMemberValue(modifier, "stats");

        try
        {
            foreach (var stat in EnumerateValues(stats, 8))
            {
                var statType = ReadMemberValue(stat, "StatType");
                if (statType != null)
                {
                    return statType;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static void AddDebugValue(Dictionary<string, string?> result, string key, object? value)
    {
        var rendered = SafeToString(value);
        if (!string.IsNullOrWhiteSpace(rendered))
        {
            result[key] = rendered;
        }
    }

    private static object? ReadEnumUnderlyingValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            var type = value.GetType();
            if (type.IsEnum)
            {
                return Convert.ChangeType(value, Enum.GetUnderlyingType(type));
            }

            return ReadMemberValue(value, "value__")
                ?? ReadMemberValue(value, "_value")
                ?? ReadMemberValue(value, "m_value")
                ?? ReadMemberValue(value, "Value");
        }
        catch
        {
            return null;
        }
    }

    private static void AddKnownMemberDebug(Dictionary<string, string?> result, object? source, string prefix)
    {
        AddDebugValue(result, $"{prefix}TypeName", source?.GetType().FullName ?? source?.GetType().Name);
        AddDebugValue(result, $"{prefix}MemberNames", DescribeMemberNames(source));
        AddDebugValue(result, $"{prefix}MethodSignatures", DescribeMethodSignatures(source, 80));

        foreach (var memberName in new[]
        {
            "StatType",
            "statType",
            "Stat",
            "stat",
            "Stats",
            "stats",
            "Name",
            "name",
            "Type",
            "type",
            "Id",
            "id"
        })
        {
            var value = ReadMemberValue(source, memberName);
            var rendered = SafeToString(value);
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                result[$"{prefix}{memberName}"] = rendered;
            }
        }
    }

    private static void AddAttachedStatsDebug(Dictionary<string, string?> result, object? modifier)
    {
        var stats = ReadMemberValue(modifier, "stats");
        if (stats == null)
        {
            return;
        }

        AddDebugValue(result, "modifierstatsTypeName", stats.GetType().FullName ?? stats.GetType().Name);
        AddDebugValue(result, "modifierstatsMemberNames", DescribeMemberNames(stats));
        AddDebugValue(result, "modifierstatsCount", ReadCollectionCount(stats));

        try
        {
            var index = 0;
            foreach (var stat in EnumerateValues(stats, 4))
            {
                var prefix = $"attachedStats{index}";
                AddDebugValue(result, $"{prefix}Type", stat?.GetType().Name);
                AddDebugValue(result, $"{prefix}TypeName", stat?.GetType().FullName ?? stat?.GetType().Name);
                AddDebugValue(result, $"{prefix}MemberNames", DescribeMemberNames(stat));
                AddDebugValue(result, $"{prefix}Text", stat);
                AddDebugValue(result, $"{prefix}StatType", ReadMemberValue(stat, "StatType"));
                AddDebugValue(result, $"{prefix}StatTypeUnderlying", ReadEnumUnderlyingValue(ReadMemberValue(stat, "StatType")));
                AddDebugValue(result, $"{prefix}Value", ReadMemberValue(stat, "Value"));
                AddDebugValue(result, $"{prefix}CurrentValue", ReadMemberValue(stat, "CurrentValue"));
                AddDebugValue(result, $"{prefix}BaseValue", ReadMemberValue(stat, "BaseValue"));
                AddKnownMemberDebug(result, stat, prefix);
                index++;
            }

            result["attachedStatsCountSampled"] = index.ToString();
        }
        catch
        {
        }
    }

    private static IEnumerable<object?> EnumerateValues(object? values, int maxItems)
    {
        if (values == null || values is string)
        {
            yield break;
        }

        var count = 0;
        if (values is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (count++ >= maxItems)
                {
                    yield break;
                }

                yield return item;
            }

            yield break;
        }

        var collectionCount = ReadCollectionCount(values);
        if (collectionCount == null)
        {
            yield break;
        }

        for (var index = 0; index < collectionCount.Value && index < maxItems; index++)
        {
            var item = ReadIndexedValue(values, index);
            if (item != null)
            {
                yield return item;
            }
        }
    }

    private static int? ReadCollectionCount(object? source)
    {
        var count = ReadMemberValue(source, "Count")
            ?? ReadMemberValue(source, "count")
            ?? InvokeMemberMethod(source, "get_Count");

        return int.TryParse(SafeToString(count), out var parsed) ? parsed : null;
    }

    private static object? ReadIndexedValue(object? source, int index)
    {
        return InvokeMemberMethod(source, "get_Item", index)
            ?? InvokeMemberMethod(source, "get_Item", (uint)index)
            ?? InvokeMemberMethod(source, "get_Item", (long)index)
            ?? InvokeMemberMethod(source, "get_Item", (ulong)index)
            ?? InvokeMemberMethod(source, "get_Item", index.ToString());
    }

    private static object? InvokeMemberMethod(object? source, string methodName, params object[] args)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            var argTypes = args.Select(arg => arg.GetType()).ToArray();
            var method = source.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, argTypes, null);
            if (method != null)
            {
                return method.Invoke(source, args);
            }

            foreach (var candidate in source.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!candidate.Name.Equals(methodName, StringComparison.Ordinal) || candidate.GetParameters().Length != args.Length)
                {
                    continue;
                }

                try
                {
                    return candidate.Invoke(source, args);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private sealed record FieldReadResult(object? Value, string? Trace);

    private static FieldReadResult ReadIl2CppField(object? source, string fieldName)
    {
        if (source == null)
        {
            return new FieldReadResult(null, "source:null");
        }

        var trace = new List<string>();
        try
        {
            var method = source.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                    candidate.Name.Equals("FieldGetter", StringComparison.Ordinal)
                    && candidate.GetParameters().Length == 3);

            if (method == null)
            {
                return new FieldReadResult(null, "method:missing");
            }

            foreach (var typeName in BuildIl2CppTypeNameCandidates(source))
            {
                var args = new object?[] { typeName, fieldName, null };

                try
                {
                    method.Invoke(source, args);
                    if (args[2] != null)
                    {
                        trace.Add($"{typeName}:hit:{args[2].GetType().FullName ?? args[2].GetType().Name}");
                        return new FieldReadResult(args[2], string.Join(" | ", trace));
                    }

                    trace.Add($"{typeName}:null");
                }
                catch (Exception ex)
                {
                    trace.Add($"{typeName}:error:{ex.GetType().Name}:{ex.InnerException?.GetType().Name ?? ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            trace.Add($"outer:error:{ex.GetType().Name}:{ex.Message}");
        }

        return new FieldReadResult(null, string.Join(" | ", trace));
    }

    private static IEnumerable<string> BuildIl2CppTypeNameCandidates(object source)
    {
        var type = source.GetType();
        var names = new[]
        {
            type.FullName,
            type.Name,
            type.FullName?.Split('[').FirstOrDefault(),
            type.Name.Split('[').FirstOrDefault(),
            type.FullName?.Replace("Il2CppSystem.", "System."),
            type.Name.Replace("Il2CppSystem.", "System.")
        };

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal);
    }

    private static void AddMember(Dictionary<string, string?> result, object? source, string key, string memberName)
    {
        var value = ReadMemberValue(source, memberName);
        var rendered = SafeToString(value);
        if (!string.IsNullOrWhiteSpace(rendered))
        {
            result[key] = rendered;
        }
    }

    private static object? InvokeNoArgumentMethod(object? source, string methodName)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            var method = source.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                    candidate.Name.Equals(methodName, StringComparison.Ordinal)
                    && candidate.GetParameters().Length == 0);

            return method?.Invoke(source, Array.Empty<object?>());
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadMemberValue(object? source, string memberName)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            var type = source.GetType();
            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(source);
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(source);
        }
        catch
        {
            return null;
        }
    }

    private static string? DescribeMemberNames(object? source)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            var names = source.GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public)
                .Where(member => member.MemberType is MemberTypes.Property or MemberTypes.Field or MemberTypes.Method)
                .Select(member => member.Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .Take(80);

            return string.Join(",", names);
        }
        catch
        {
            return null;
        }
    }

    private static string? DescribeMethodSignatures(object? source, int maxMethods)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            var signatures = source.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .Take(maxMethods)
                .Select(MethodSignature);

            return string.Join(" | ", signatures);
        }
        catch
        {
            return null;
        }
    }

    private static string? DescribeCollectionSample(object? values, int maxItems)
    {
        if (values == null)
        {
            return null;
        }

        try
        {
            var parts = new List<string>();
            foreach (var value in EnumerateValues(values, maxItems))
            {
                parts.Add(DescribeObjectMembers(value, 12) ?? SafeToString(value) ?? "");
            }

            return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        catch
        {
            return null;
        }
    }

    private static string? DescribeObjectMembers(object? source, int maxMembers)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            if (IsSimple(source.GetType()))
            {
                return SafeToString(source);
            }

            var parts = new List<string>();
            foreach (var member in source.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public)
                         .Where(member => member.MemberType is MemberTypes.Property or MemberTypes.Field)
                         .OrderBy(member => member.Name, StringComparer.Ordinal)
                         .Take(maxMembers))
            {
                object? memberValue = null;
                try
                {
                    memberValue = member switch
                    {
                        PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(source),
                        FieldInfo field when !field.IsStatic => field.GetValue(source),
                        _ => null
                    };
                }
                catch
                {
                }

                var rendered = SafeToString(memberValue);
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    parts.Add($"{member.Name}={rendered}");
                }
            }

            return string.Join(",", parts);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeModifierKey(string key)
    {
        return key switch
        {
            "Stat" => "stat",
            "Amount" => "amount",
            "ModifierType" => "modifierType",
            "MultiplierType" => "multiplierType",
            "BaneRace" => "baneRace",
            "BaneKind" => "baneKind",
            "Modifier" => "modifier",
            "Item1" => "stat",
            "Item2" => "modifier",
            _ => string.IsNullOrEmpty(key) ? key : char.ToLowerInvariant(key[0]) + key[1..]
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> SerializeGenericEntries(System.Collections.IEnumerable? values)
    {
        var entries = new List<IReadOnlyDictionary<string, string?>>();
        if (values == null)
        {
            return entries;
        }

        foreach (var item in values)
        {
            var details = DescribeObject(item);
            if (details.Count > 0)
            {
                entries.Add(details);
            }
        }

        return entries;
    }

    private static IReadOnlyDictionary<string, string?> DescribeObject(object? value)
    {
        if (value == null)
        {
            return new Dictionary<string, string?>();
        }

        var type = value.GetType();
        if (IsSimple(type))
        {
            return new Dictionary<string, string?> { ["value"] = value.ToString() };
        }

        try
        {
            var result = new Dictionary<string, string?> { ["type"] = type.Name };
            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
            {
                if (member.MemberType is not (MemberTypes.Property or MemberTypes.Field))
                {
                    continue;
                }

                object? memberValue = null;
                try
                {
                    memberValue = member switch
                    {
                        PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(value),
                        FieldInfo field => field.GetValue(value),
                        _ => null
                    };
                }
                catch
                {
                }

                if (memberValue == null)
                {
                    continue;
                }

                var rendered = SafeToString(memberValue);
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    result[member.Name] = rendered;
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string?> { ["value"] = value.ToString() };
        }
    }

    private static string DescribeObjectText(object? value)
    {
        var details = DescribeObject(value);
        return string.Join(", ", details.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string? SafeToString(object? value)
    {
        try
        {
            var nullableValue = ReadNullableValue(value);
            if (nullableValue != null)
            {
                return nullableValue.ToString();
            }

            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadNullableValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            var type = value.GetType();
            if (!type.FullName?.StartsWith("Il2CppSystem.Nullable`1", StringComparison.Ordinal) ?? true)
            {
                return null;
            }

            var hasValue = ReadMemberValue(value, "HasValue");
            if (hasValue is bool typedHasValue && !typedHasValue)
            {
                return null;
            }

            return ReadMemberValue(value, "Value");
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, object?> SerializeObjectGraph(object? value, int maxDepth = 2)
    {
        var graph = SerializeNode(value, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return graph as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>
        {
            ["value"] = graph
        };
    }

    private static object? SerializeNode(object? value, int depth, HashSet<object> seen)
    {
        if (value == null)
        {
            return null;
        }

        var type = value.GetType();
        if (IsSimple(type))
        {
            return value.ToString();
        }

        if (depth <= 0)
        {
            return type.FullName ?? type.Name;
        }

        if (!type.IsValueType)
        {
            if (!seen.Add(value))
            {
                return $"<cycle {type.Name}>";
            }
        }

        try
        {
            if (value is IEnumerable enumerable && value is not string)
            {
                var items = new List<object?>();
                foreach (var item in enumerable)
                {
                    items.Add(SerializeNode(item, depth - 1, seen));
                }

                return new Dictionary<string, object?>
                {
                    ["__type"] = type.FullName ?? type.Name,
                    ["__kind"] = "enumerable",
                    ["__count"] = items.Count.ToString(),
                    ["items"] = items
                };
            }

            var result = new Dictionary<string, object?>
            {
                ["__type"] = type.FullName ?? type.Name,
                ["__kind"] = "object"
            };

            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
            {
                if (member.MemberType is not (MemberTypes.Property or MemberTypes.Field))
                {
                    continue;
                }

                object? memberValue = null;
                try
                {
                    memberValue = member switch
                    {
                        PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(value),
                        FieldInfo field when !field.IsStatic => field.GetValue(value),
                        _ => null
                    };
                }
                catch
                {
                }

                if (memberValue == null)
                {
                    continue;
                }

                result[member.Name] = SerializeNode(memberValue, depth - 1, seen);
            }

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(MethodSignature)
                .ToList();
            if (methods.Count > 0)
            {
                result["__methods"] = methods;
            }

            return result;
        }
        finally
        {
            if (!type.IsValueType)
            {
                seen.Remove(value);
            }
        }
    }

    private static string MethodSignature(MethodInfo method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));
        return $"{method.ReturnType.Name} {method.Name}({parameters})";
    }

    private static bool IsSimple(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
