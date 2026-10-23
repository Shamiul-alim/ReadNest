namespace ReadNest.Web.Components.Shared;

/// <summary>One choice of an <see cref="AppSelect{TValue}"/>: the bound value and the text shown for it.</summary>
public sealed record SelectOption<TValue>(TValue Value, string Label, bool Disabled = false, string? Hint = null);

public static class SelectOption
{
    public static SelectOption<TValue> Of<TValue>(TValue value, string label, bool disabled = false, string? hint = null) =>
        new(value, label, disabled, hint);

    /// <summary>Every value of an enum, labelled with <paramref name="label"/> (for example ToDisplayName).</summary>
    public static IReadOnlyList<SelectOption<TEnum>> ForEnum<TEnum>(Func<TEnum, string> label)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(v => new SelectOption<TEnum>(v, label(v))).ToList();

    /// <summary>Every value of an enum as nullable options, for selects that also allow "none".</summary>
    public static IReadOnlyList<SelectOption<TEnum?>> ForNullableEnum<TEnum>(Func<TEnum, string> label)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(v => new SelectOption<TEnum?>(v, label(v))).ToList();
}
