using System.Globalization;
using System.Text.Json;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Catalogue;

/// <summary>
/// Binds JSON to a parameter schema. Every issue found at a level is reported
/// before the binder gives up on that level; nothing here throws for bad input.
/// </summary>
public static class ParameterBinder
{
    private const string TypeKey = "type";

    /// <summary>
    /// Returns the bound values, or null exactly when it added to <paramref name="issues"/>.
    /// With <paramref name="construct"/> false, references are only checked to be
    /// strings and object factories are not called — the result is for checking,
    /// not for a factory.
    /// </summary>
    public static ParameterValues? Bind(
        IReadOnlyList<ParameterDescriptor> schema,
        JsonElement json,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(issues);
        return BindObject(schema, json, path, context, construct, issues, allowTypeKey: false);
    }

    private static ParameterValues? BindObject(
        IReadOnlyList<ParameterDescriptor> schema,
        JsonElement json,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        bool allowTypeKey)
    {
        int before = issues.Count;
        bool absent = json.ValueKind == JsonValueKind.Undefined;
        if (!absent && json.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Bad(path, $"This must be an object, but it is {Describe(json)}.", "Write it as { … } with one key per parameter."));
            return null;
        }

        if (!absent)
        {
            foreach (JsonProperty property in json.EnumerateObject())
            {
                bool declared = schema.Any(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));
                if (!declared && !(allowTypeKey && string.Equals(property.Name, TypeKey, StringComparison.Ordinal)))
                {
                    issues.Add(new BindingIssue(
                        BindingIssueKind.UnknownKey,
                        $"{path}.{property.Name}",
                        $"'{property.Name}' is not a parameter here.",
                        Suggest.Fix(property.Name, schema.Select(p => p.Name), "parameters")));
                }
            }
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Material states index a sibling material and values convert to a sibling tag's kind, so both bind last.
        foreach (ParameterDescriptor parameter in schema.OrderBy(p => p.Kind is ParameterKind.MaterialState or ParameterKind.Value ? 1 : 0))
        {
            string childPath = $"{path}.{parameter.Name}";
            bool present = !absent && json.TryGetProperty(parameter.Name, out _);
            if (!present)
            {
                BindAbsent(parameter, childPath, context, construct, issues, values);
                continue;
            }

            JsonElement element = json.GetProperty(parameter.Name);
            if (TryBindValue(parameter, element, childPath, context, construct, issues, values, json, out object? bound))
            {
                values[parameter.Name] = bound;
            }
        }

        return issues.Count == before ? new ParameterValues(values, schema.Select(p => p.Name)) : null;
    }

    private static void BindAbsent(
        ParameterDescriptor parameter,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        Dictionary<string, object?> values)
    {
        if (parameter.IsRequired)
        {
            issues.Add(Bad(path, $"'{parameter.Name}' is required and has no default.", $"Add \"{parameter.Name}\": {Example(parameter)}."));
            return;
        }

        switch (parameter.Kind)
        {
            case ParameterKind.Group:
                values[parameter.Name] = BindObject(parameter.Children, default, path, context, construct, issues, allowTypeKey: false);
                break;
            case ParameterKind.GroupList:
                values[parameter.Name] = (IReadOnlyList<ParameterValues>)[];
                break;
            case ParameterKind.ObjectList:
                values[parameter.Name] = (IReadOnlyList<object>)[];
                break;
            case ParameterKind.StringList:
                values[parameter.Name] = (IReadOnlyList<string>)[];
                break;
            case ParameterKind.Int when parameter.Default is long whole:
                values[parameter.Name] = (int)whole;
                break;
            default:
                if (parameter.Default is not null)
                {
                    values[parameter.Name] = parameter.Default;
                }

                break;
        }
    }

    private static bool TryBindValue(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        Dictionary<string, object?> siblings,
        JsonElement objectJson,
        out object? bound)
    {
        bound = null;
        switch (parameter.Kind)
        {
            case ParameterKind.Double:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double number) || !double.IsFinite(number))
                {
                    issues.Add(WrongType(path, parameter, "a number", element));
                    return false;
                }

                bound = number;
                return InRange(parameter, number, path, issues);

            case ParameterKind.Int:
                if (element.ValueKind != JsonValueKind.Number)
                {
                    issues.Add(WrongType(path, parameter, "a whole number", element));
                    return false;
                }

                if (!element.TryGetInt64(out long whole) || whole < int.MinValue || whole > int.MaxValue)
                {
                    issues.Add(Bad(path, $"'{parameter.Name}' must be a whole number, but it is {element.GetRawText()}.", "Remove the fraction, or pick a value that fits in 32 bits."));
                    return false;
                }

                bound = (int)whole;
                return InRange(parameter, whole, path, issues);

            case ParameterKind.Bool:
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    issues.Add(WrongType(path, parameter, "true or false", element));
                    return false;
                }

                bound = element.GetBoolean();
                return true;

            case ParameterKind.String:
                if (element.ValueKind != JsonValueKind.String)
                {
                    issues.Add(WrongType(path, parameter, "a string", element));
                    return false;
                }

                bound = element.GetString()!;
                return true;

            case ParameterKind.StringList:
                return TryBindList(
                    parameter, element, path, issues,
                    (item, itemPath) =>
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            return item.GetString()!;
                        }

                        issues.Add(Bad(itemPath, $"Every entry of '{parameter.Name}' must be a string, but this is {Describe(item)}.", "Write it as \"…\"."));
                        return null;
                    },
                    items => (IReadOnlyList<string>)items.Cast<string>().ToList(),
                    out bound);

            case ParameterKind.Enum:
                if (element.ValueKind != JsonValueKind.String)
                {
                    issues.Add(WrongType(path, parameter, "a string", element));
                    return false;
                }

                string choice = element.GetString()!;
                if (!parameter.AllowedValues.Contains(choice, StringComparer.Ordinal))
                {
                    issues.Add(Bad(path, $"'{choice}' is not a value '{parameter.Name}' allows.", Suggest.Fix(choice, parameter.AllowedValues, "values")));
                    return false;
                }

                bound = choice;
                return true;

            case ParameterKind.Group:
                bound = BindObject(parameter.Children, element, path, context, construct, issues, allowTypeKey: false);
                return bound is not null;

            case ParameterKind.GroupList:
                return TryBindList(
                    parameter, element, path, issues,
                    (item, itemPath) => BindObject(parameter.Children, item, itemPath, context, construct, issues, allowTypeKey: false),
                    items => (IReadOnlyList<ParameterValues>)items.Cast<ParameterValues>().ToList(),
                    out bound);

            case ParameterKind.ObjectList:
                return TryBindList(
                    parameter, element, path, issues,
                    (item, itemPath) => BindSlotObject(parameter.Slot, item, itemPath, context, construct, issues),
                    items => (IReadOnlyList<object>)items,
                    out bound);

            case ParameterKind.Object:
                bound = BindSlotObject(parameter.Slot, element, path, context, construct, issues);
                return bound is not null;

            case ParameterKind.Reference:
                return TryBindReference(parameter, element, path, context, construct, issues, out bound);

            case ParameterKind.Material:
                return TryBindMaterial(parameter, element, path, context, issues, out bound);

            case ParameterKind.MaterialState:
                return TryBindState(parameter, element, path, issues, siblings, objectJson, out bound);

            case ParameterKind.Tag:
                return TryBindTag(parameter, element, path, context, construct, issues, out bound);

            case ParameterKind.Value:
                return TryBindTagValue(parameter, element, path, context, construct, issues, siblings, out bound);

            default:
                throw new InvalidOperationException($"Parameter kind {parameter.Kind} has no binder.");
        }
    }

    private static bool TryBindList(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        List<BindingIssue> issues,
        Func<JsonElement, string, object?> bindItem,
        Func<List<object>, object> wrap,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(WrongType(path, parameter, "an array", element));
            return false;
        }

        int length = element.GetArrayLength();
        if (length < parameter.MinCount)
        {
            issues.Add(Bad(
                path,
                string.Create(CultureInfo.InvariantCulture, $"'{parameter.Name}' needs at least {parameter.MinCount} entries, but it has {length}."),
                "Add the missing entries."));
            return false;
        }

        var items = new List<object>(length);
        bool ok = true;
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            object? value = bindItem(item, string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"));
            if (value is null)
            {
                ok = false;
            }
            else
            {
                items.Add(value);
            }

            index++;
        }

        if (ok)
        {
            bound = wrap(items);
        }

        return ok;
    }

    /// <summary>Returns the constructed object; in check mode, a placeholder; null when it added an issue.</summary>
    private static object? BindSlotObject(
        string slot,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(TypeKey, out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            issues.Add(Bad(
                path,
                $"A {slot} is an object with a string \"type\" key, but this is {Describe(element)} without one.",
                $"Write {{ \"type\": \"…\", … }} using one of {Suggest.List(context.Catalogue.ObjectsIn(slot).Select(o => o.Type))}."));
            return null;
        }

        string type = typeElement.GetString()!;
        if (!context.Catalogue.TryGetObject(slot, type, out ObjectDescriptor? descriptor))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.UnknownType,
                $"{path}.{TypeKey}",
                $"'{type}' is not a {slot} in this catalogue.",
                Suggest.Fix(type, context.Catalogue.ObjectsIn(slot).Select(o => o.Type), $"{slot}s")));
            return null;
        }

        ParameterValues? values = BindObject(descriptor.Parameters, element, path, context, construct, issues, allowTypeKey: true);
        if (values is null)
        {
            return null;
        }

        if (!construct)
        {
            return descriptor;
        }

        try
        {
            return descriptor.Factory(values);
        }
        catch (ArgumentException ex)
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.Rejected,
                path,
                $"The {slot} '{type}' rejected its parameters: {ex.Message}",
                "Change the parameter the message names."));
            return null;
        }
    }

    private static bool TryBindReference(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a component id (a string)", element));
            return false;
        }

        string id = element.GetString()!;
        if (!construct)
        {
            bound = id;
            return true;
        }

        if (!context.TryGetNode(id, out ISimNode? node))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.MissingReference,
                path,
                $"'{id}' is not a component in this plant.",
                Suggest.Fix(id, context.NodeIds, "components")));
            return false;
        }

        if (!Capabilities.TryGet(node, parameter.Capability!, out bound))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.MissingCapability,
                path,
                $"'{id}' cannot supply {parameter.Capability!.Name}, which '{parameter.Name}' needs.",
                "Reference a component that can."));
            return false;
        }

        return true;
    }

    private static bool TryBindMaterial(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        List<BindingIssue> issues,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a material name (a string)", element));
            return false;
        }

        string name = element.GetString()!;
        if (!context.TryGetMaterial(name, out MaterialDescriptor? material))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.UnknownMaterial,
                path,
                $"'{name}' is not a material in this plant or its catalogue.",
                Suggest.Fix(name, context.MaterialNames, "materials")));
            return false;
        }

        if (parameter.Payload is { } required && material.Material.Kind != required)
        {
            issues.Add(Bad(
                path,
                $"'{name}' is a {material.Material.Kind} material, but '{parameter.Name}' needs a {required} one.",
                $"Name a {required} material."));
            return false;
        }

        bound = material;
        return true;
    }

    private static bool TryBindState(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        List<BindingIssue> issues,
        Dictionary<string, object?> siblings,
        JsonElement objectJson,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a state name (a string)", element));
            return false;
        }

        if (!siblings.TryGetValue(parameter.MaterialParameter, out object? sibling) || sibling is not MaterialDescriptor material)
        {
            // CatalogueBuilder guarantees the sibling is a declared Material parameter, so the
            // only way it is absent from `siblings` here is that it failed to bind (already
            // reported) or is optional and was left out.
            bool materialGiven = objectJson.ValueKind == JsonValueKind.Object
                && objectJson.TryGetProperty(parameter.MaterialParameter, out _);
            if (!materialGiven)
            {
                issues.Add(Bad(
                    path,
                    $"'{parameter.Name}' names a state of '{parameter.MaterialParameter}', which was not given.",
                    $"Give '{parameter.MaterialParameter}' as well, or remove '{parameter.Name}'."));
            }

            return false;
        }

        string state = element.GetString()!;
        int index = -1;
        for (int i = 0; i < material.Material.StateSchema.Count; i++)
        {
            if (string.Equals(material.Material.StateSchema[i], state, StringComparison.Ordinal))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.UnknownState,
                path,
                $"Material '{material.Material.Name}' has no state named '{state}'.",
                Suggest.Fix(state, material.Material.StateSchema, "states")));
            return false;
        }

        bound = index;
        return true;
    }

    private static bool TryBindTag(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a tag name (a string)", element));
            return false;
        }

        string name = element.GetString()!;
        if (construct && context.ResolvesTags)
        {
            if (!context.TryGetTag(name, out TagKind kind, out TagAccess access))
            {
                string? closest = Suggest.Closest(name, context.TagNames);
                issues.Add(new BindingIssue(
                    BindingIssueKind.UnknownTag,
                    path,
                    $"'{name}' is not a tag in this plant.",
                    closest is null
                        ? "Use a tag the plant has; `dse tags` lists them."
                        : $"Use a tag the plant has — '{closest}' is closest."));
                return false;
            }

            if (parameter.RequiredKind is { } required && kind != required)
            {
                issues.Add(new BindingIssue(
                    BindingIssueKind.WrongTagKind,
                    path,
                    $"'{name}' is {A(kind)} tag, but '{parameter.Name}' needs {A(required)} tag.",
                    $"Name {A(required)} tag; `dse tags` lists every tag with its kind."));
                return false;
            }

            if (parameter.IsWriteTarget && access != TagAccess.ReadWrite)
            {
                issues.Add(new BindingIssue(
                    BindingIssueKind.ReadOnlyTag,
                    path,
                    $"'{name}' is read-only, so a block cannot command it.",
                    "Command a read-write tag; `dse tags` shows each tag's access. An input a signal link drives is read-only."));
                return false;
            }
        }

        bound = name;
        return true;
    }

    private static bool TryBindTagValue(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        Dictionary<string, object?> siblings,
        out object? bound)
    {
        bound = null;
        bool isBool = element.ValueKind is JsonValueKind.True or JsonValueKind.False;
        double number = 0.0;
        if (!isBool && (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out number) || !double.IsFinite(number)))
        {
            issues.Add(WrongType(path, parameter, "true, false or a number", element));
            return false;
        }

        if (!construct || !context.ResolvesTags)
        {
            // R84: nothing to convert against, so the value is provisional.
            bound = isBool ? TagValue.Bool(element.GetBoolean()) : TagValue.Double(number);
            return true;
        }

        if (!siblings.TryGetValue(parameter.TagParameter, out object? sibling)
            || sibling is not string tag
            || !context.TryGetTag(tag, out TagKind kind, out _))
        {
            // The tag did not resolve, and that is already reported: one mistake, one diagnostic.
            return false;
        }

        TagValue? converted = kind switch
        {
            TagKind.Bool => isBool ? TagValue.Bool(element.GetBoolean()) : (TagValue?)null,
            TagKind.Int64 => !isBool && TryWhole(element, number, out long whole) ? TagValue.Int64(whole) : (TagValue?)null,
            _ => isBool ? (TagValue?)null : TagValue.Double(number),
        };

        if (converted is not { } value)
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.WrongTagKind,
                path,
                $"{element.GetRawText()} does not fit '{tag}', which is {A(kind)} tag.",
                kind switch
                {
                    TagKind.Bool => "Write true or false.",
                    TagKind.Int64 => "Write a whole number.",
                    _ => "Write a number.",
                }));
            return false;
        }

        bound = value;
        return true;
    }

    /// <summary>An integer-valued JSON number — <c>3</c>, <c>3.0</c>, <c>3e0</c> — that fits in 64 bits.</summary>
    private static bool TryWhole(JsonElement element, double number, out long whole)
    {
        if (element.TryGetInt64(out whole))
        {
            return true;
        }

        // TryGetInt64 refuses "3.0" and "3e0" (measured); the double path accepts them.
        if (Math.Floor(number) == number && number >= -9.2233720368547758E18 && number < 9.2233720368547758E18)
        {
            whole = (long)number;
            return true;
        }

        whole = 0L;
        return false;
    }

    private static string A(TagKind kind) => kind == TagKind.Int64 ? "an Int64" : $"a {kind}";

    private static bool InRange(ParameterDescriptor parameter, double value, string path, List<BindingIssue> issues)
    {
        bool below = parameter.Minimum is { } min && (parameter.ExclusiveMinimum ? value <= min : value < min);
        bool above = parameter.Maximum is { } max && (parameter.ExclusiveMaximum ? value >= max : value > max);
        if (!below && !above)
        {
            return true;
        }

        issues.Add(Bad(
            path,
            string.Create(CultureInfo.InvariantCulture, $"'{parameter.Name}' is {value}, which is outside {Range(parameter)}."),
            $"Use a value in {Range(parameter)}."));
        return false;
    }

    /// <summary>Interval notation: <c>(0, 1]</c>, <c>[0, ∞)</c>.</summary>
    internal static string Range(ParameterDescriptor parameter)
    {
        string low = parameter.Minimum is { } min
            ? string.Create(CultureInfo.InvariantCulture, $"{(parameter.ExclusiveMinimum ? '(' : '[')}{min}")
            : "(-∞";
        string high = parameter.Maximum is { } max
            ? string.Create(CultureInfo.InvariantCulture, $"{max}{(parameter.ExclusiveMaximum ? ')' : ']')}")
            : "∞)";
        return $"{low}, {high}";
    }

    private static BindingIssue Bad(string path, string message, string fix) =>
        new(BindingIssueKind.BadParameter, path, message, fix);

    private static BindingIssue WrongType(string path, ParameterDescriptor parameter, string wanted, JsonElement actual) =>
        Bad(path, $"'{parameter.Name}' must be {wanted}, but it is {Describe(actual)}.", $"Write {Example(parameter)}.");

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "nothing",
    };

    private static string Example(ParameterDescriptor parameter) => parameter.Kind switch
    {
        ParameterKind.Double => parameter.Unit.Length > 0 ? $"a number in {parameter.Unit}" : "a number",
        ParameterKind.Int => "a whole number",
        ParameterKind.Bool => "true or false",
        ParameterKind.String => "\"…\"",
        ParameterKind.Enum => $"one of {Suggest.List(parameter.AllowedValues)}",
        ParameterKind.Group => "{ … }",
        ParameterKind.GroupList or ParameterKind.ObjectList => "[ … ]",
        ParameterKind.StringList => "[ \"…\" ]",
        ParameterKind.Reference => "the id of another component",
        ParameterKind.Material => "the name of a material",
        ParameterKind.MaterialState => "the name of one of the material's states",
        ParameterKind.Object => "{ \"type\": \"…\", … }",
        ParameterKind.Tag => "the full name of a tag, such as CV001.Start",
        ParameterKind.Value => "true, false or a number",
        _ => "a value",
    };
}
