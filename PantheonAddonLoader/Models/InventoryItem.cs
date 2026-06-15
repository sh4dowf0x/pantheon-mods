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

    public Guid Id => Guid.Parse(_item.ItemInstanceGuid.ToString());
    public string Name => _item.Template.ItemName;

    public ItemSnapshot GetSnapshot(bool includeRawDump = false)
    {
        var template = _item.Template;
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
            ["availableSpaceOnStack"] = SafeString(() => _item.GetAvailableSpaceOnStack())
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
            ["allowedRaces"] = SafeString(() => template.AllowedRaces),
            ["allowedClasses"] = SafeString(() => template.AllowedClasses),
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

        var statModifiers = SerializeStatModifiers(template.StatModifiers);
        var multiplierModifiers = SerializeMultiplierModifiers(template.MultiplierModifiers);
        var requirementOverrides = SerializeGenericEntries(template.RequirementOverrides);
        var rawItemDump = includeRawDump ? SerializeObjectGraph(_item) : new Dictionary<string, object?>();
        var rawTemplateDump = includeRawDump ? SerializeObjectGraph(template) : new Dictionary<string, object?>();

        return new ItemSnapshot(
            InstanceId: Id,
            ItemId: _item.ItemId,
            Name: Name,
            StackSize: _item.StackSize,
            SlotType: SafeString(() => _item.SlotType) ?? "",
            SlotIndex: _item.SlotIndex,
            CharacterId: _item.CharacterId,
            CorpseId: _item.CorpseID,
            ParentGuid: _item.ParentGuid.ToString(),
            ModelName: _item.ModelName,
            Instance: instanceFields,
            Template: templateFields,
            StatModifiers: statModifiers,
            MultiplierModifiers: multiplierModifiers,
            RequirementOverrides: requirementOverrides,
            RawItemDump: rawItemDump,
            RawTemplateDump: rawTemplateDump);
    }

    private static string? SafeString(Func<object?> read)
    {
        try
        {
            return read()?.ToString();
        }
        catch
        {
            return null;
        }
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

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> SerializeStatModifiers(System.Collections.IEnumerable? values)
    {
        var entries = new List<IReadOnlyDictionary<string, string?>>();
        if (values == null)
        {
            return entries;
        }

        foreach (var item in values)
        {
            if (item is not StatModifier modifier)
            {
                continue;
            }

            entries.Add(new Dictionary<string, string?>
            {
                ["stat"] = SafeString(() => modifier.Stat),
                ["amount"] = SafeString(() => modifier.Amount),
                ["modifierType"] = SafeString(() => modifier.ModifierType)
            });
        }

        return entries;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> SerializeMultiplierModifiers(System.Collections.IEnumerable? values)
    {
        var entries = new List<IReadOnlyDictionary<string, string?>>();
        if (values == null)
        {
            return entries;
        }

        foreach (var item in values)
        {
            if (item is not MultiplierModifier modifier)
            {
                continue;
            }

            entries.Add(new Dictionary<string, string?>
            {
                ["amount"] = SafeString(() => modifier.Amount),
                ["multiplierType"] = SafeString(() => modifier.MultiplierType),
                ["baneRace"] = SafeString(() => modifier.BaneRace),
                ["baneKind"] = SafeString(() => modifier.BaneKind),
                ["modifier"] = SafeString(() => modifier.Modifier)
            });
        }

        return entries;
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

                var rendered = memberValue.ToString();
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
