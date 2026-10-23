using ReadNest.Domain.Enums;
using ReadNest.Web.Components.Shared;

namespace ReadNest.ComponentTests;

/// <summary>
/// AppSelect's .NET behaviour: rendering, ARIA state, value binding, disabled options, validation, filtering and
/// clearing. Keyboard handling, focus, viewport placement and outside clicks live in js/select.js and are covered
/// by the browser smoke test (tests/e2e/ui-smoke.mjs).
/// </summary>
public sealed class AppSelectTests : BunitContext
{
    private static readonly IReadOnlyList<SelectOption<ReadingStatus>> Statuses = SelectOption.ForEnum<ReadingStatus>(s => s.ToDisplayName());

    public AppSelectTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Shows_the_selected_label_and_a_closed_combobox()
    {
        var cut = Render<AppSelect<ReadingStatus>>(p => p
            .Add(x => x.Options, Statuses)
            .Add(x => x.Value, ReadingStatus.Paused)
            .Add(x => x.Id, "status"));

        var trigger = cut.Find("#status");
        Assert.Equal("combobox", trigger.GetAttribute("role"));
        Assert.Equal("listbox", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Contains("Paused", trigger.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[role=option]"));
    }

    [Fact]
    public void Shows_the_placeholder_when_the_value_matches_no_option()
    {
        var cut = Render<AppSelect<string>>(p => p
            .Add(x => x.Options, [SelectOption.Of("a", "Alpha")])
            .Add(x => x.Value, "missing")
            .Add(x => x.Placeholder, "Pick one"));

        var value = cut.Find(".rn-select-value");
        Assert.Equal("Pick one", value.TextContent);
        Assert.Contains("is-placeholder", value.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_lists_options_with_the_selected_one_marked()
    {
        var cut = Render<AppSelect<ReadingStatus>>(p => p
            .Add(x => x.Options, Statuses)
            .Add(x => x.Value, ReadingStatus.Read));

        cut.Find(".rn-select-trigger").Click();

        Assert.Equal("true", cut.Find(".rn-select-trigger").GetAttribute("aria-expanded"));
        var options = cut.FindAll("[role=option]");
        Assert.Equal(Enum.GetValues<ReadingStatus>().Length, options.Count);
        var selected = Assert.Single(options, o => o.GetAttribute("aria-selected") == "true");
        Assert.Contains("Read", selected.TextContent, StringComparison.Ordinal);
        Assert.Equal(cut.Find("[role=listbox]").Id, cut.Find(".rn-select-trigger").GetAttribute("aria-controls"));
    }

    [Fact]
    public void Choosing_an_option_binds_the_enum_value_and_closes()
    {
        ReadingStatus? changed = null;
        var cut = Render<AppSelect<ReadingStatus>>(p => p
            .Add(x => x.Options, Statuses)
            .Add(x => x.Value, ReadingStatus.WantToRead)
            .Add(x => x.ValueChanged, (ReadingStatus v) => changed = v));

        cut.Find(".rn-select-trigger").Click();
        cut.FindAll("[role=option]").Single(o => o.TextContent.Contains("Did not finish", StringComparison.Ordinal)).Click();

        Assert.Equal(ReadingStatus.DidNotFinish, changed);
        Assert.Equal("false", cut.Find(".rn-select-trigger").GetAttribute("aria-expanded"));
        Assert.Contains("Did not finish", cut.Find(".rn-select-value").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Labels_and_values_are_independent()
    {
        int? changed = null;
        var cut = Render<AppSelect<int>>(p => p
            .Add(x => x.Options, [SelectOption.Of(0, "Any"), SelectOption.Of(4, "4 stars and up")])
            .Add(x => x.Value, 0)
            .Add(x => x.ValueChanged, (int v) => changed = v));

        cut.Find(".rn-select-trigger").Click();
        cut.FindAll("[role=option]")[1].Click();

        Assert.Equal(4, changed);
    }

    [Fact]
    public void Disabled_options_cannot_be_chosen()
    {
        var changes = 0;
        var cut = Render<AppSelect<string>>(p => p
            .Add(x => x.Options, [SelectOption.Of("a", "Alpha"), SelectOption.Of("b", "Beta", disabled: true)])
            .Add(x => x.Value, "a")
            .Add(x => x.ValueChanged, (string? _) => changes++));

        cut.Find(".rn-select-trigger").Click();
        var beta = cut.FindAll("[role=option]")[1];
        Assert.Equal("true", beta.GetAttribute("aria-disabled"));
        beta.Click();

        Assert.Equal(0, changes);
        Assert.Contains("Alpha", cut.Find(".rn-select-value").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_select_does_not_open()
    {
        var cut = Render<AppSelect<ReadingStatus>>(p => p
            .Add(x => x.Options, Statuses)
            .Add(x => x.Disabled, true));

        Assert.True(cut.Find(".rn-select-trigger").HasAttribute("disabled"));
        await cut.InvokeAsync(() => cut.Instance.OpenFromKeyboard());
        Assert.Equal("false", cut.Find(".rn-select-trigger").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task Keyboard_callbacks_open_select_by_stable_index_and_close()
    {
        string? changed = null;
        var cut = Render<AppSelect<string>>(p => p
            .Add(x => x.Options, [SelectOption.Of("en", "English"), SelectOption.Of("fr", "French"), SelectOption.Of("de", "German")])
            .Add(x => x.Value, "en")
            .Add(x => x.Searchable, true)
            .Add(x => x.ValueChanged, (string? v) => changed = v));

        await cut.InvokeAsync(() => cut.Instance.OpenFromKeyboard());
        cut.Find(".rn-select-search").Input("ger");
        var visible = cut.FindAll("[role=option]");
        Assert.Single(visible);
        Assert.Equal("2", visible[0].GetAttribute("data-index"));

        // The index refers to the full list, so it still means "German" while the list is filtered.
        await cut.InvokeAsync(() => cut.Instance.SelectIndex(2));
        Assert.Equal("de", changed);

        await cut.InvokeAsync(() => cut.Instance.OpenFromKeyboard());
        Assert.Equal("true", cut.Find(".rn-select-trigger").GetAttribute("aria-expanded"));
        await cut.InvokeAsync(() => cut.Instance.CloseFromKeyboard(true));
        Assert.Equal("false", cut.Find(".rn-select-trigger").GetAttribute("aria-expanded"));

        await cut.InvokeAsync(() => cut.Instance.OpenFromKeyboard());
        await cut.InvokeAsync(() => cut.Instance.CloseFromOutside());
        Assert.Equal("false", cut.Find(".rn-select-trigger").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Searching_with_no_matches_says_so()
    {
        var cut = Render<AppSelect<string>>(p => p
            .Add(x => x.Options, [SelectOption.Of("a", "Alpha")])
            .Add(x => x.Searchable, true));

        cut.Find(".rn-select-trigger").Click();
        cut.Find(".rn-select-search").Input("zzz");

        Assert.Empty(cut.FindAll("[role=option]"));
        Assert.Contains("No matches", cut.Find(".rn-select-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_and_empty_states_are_announced()
    {
        var loading = Render<AppSelect<string>>(p => p.Add(x => x.Options, []).Add(x => x.Loading, true));
        Assert.Equal("true", loading.Find(".rn-select-trigger").GetAttribute("aria-busy"));
        loading.Find(".rn-select-trigger").Click();
        Assert.Contains("Loading", loading.Find(".rn-select-state").TextContent, StringComparison.Ordinal);

        var empty = Render<AppSelect<string>>(p => p.Add(x => x.Options, []).Add(x => x.EmptyText, "No shelves yet"));
        empty.Find(".rn-select-trigger").Click();
        Assert.Equal("No shelves yet", empty.Find(".rn-select-state").TextContent);
    }

    [Fact]
    public void Clearable_select_resets_to_the_default_value()
    {
        Guid? changed = Guid.NewGuid();
        var id = Guid.NewGuid();
        var cut = Render<AppSelect<Guid?>>(p => p
            .Add(x => x.Options, [SelectOption.Of<Guid?>(id, "A book")])
            .Add(x => x.Value, id)
            .Add(x => x.Clearable, true)
            .Add(x => x.ValueChanged, (Guid? v) => changed = v));

        cut.Find(".rn-select-clear").Click();

        Assert.Null(changed);
    }

    [Fact]
    public void Validation_marks_the_select_invalid_until_a_value_is_chosen()
    {
        var cut = Render<SelectForm>();

        cut.Find("form").Submit();

        Assert.False(cut.Instance.Submitted);
        Assert.Contains("invalid", cut.Find(".rn-select-ctl").ClassName, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#status").GetAttribute("aria-invalid"));
        Assert.Contains("Pick a status.", cut.Markup, StringComparison.Ordinal);

        cut.Find("#status").Click();
        cut.FindAll("[role=option]").Single(o => o.TextContent.Contains("Want to read", StringComparison.Ordinal)).Click();

        Assert.Equal(ReadingStatus.WantToRead, cut.Instance.Model.Status);
        Assert.DoesNotContain("invalid", cut.Find(".rn-select-ctl").ClassName, StringComparison.Ordinal);
        Assert.Null(cut.Find("#status").GetAttribute("aria-invalid"));

        cut.Find("form").Submit();
        Assert.True(cut.Instance.Submitted);
    }
}
