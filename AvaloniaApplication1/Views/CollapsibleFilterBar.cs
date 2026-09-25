using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

/// <summary>
/// A filter row that can be folded away to a one-line summary
/// (Kompakteres-Layout.md, Teil D): a chevron on the left
/// toggles <see cref="IsExpanded"/>; collapsed, the filter controls
/// (<see cref="ContentControl.Content"/>) are replaced by
/// <see cref="Summary"/> - only the filters that actually narrow the list,
/// so a collapsed bar still shows that something is being hidden - and
/// clicking that summary expands the bar again. Look lives in
/// CollapsibleFilterBar.axaml (a ControlTheme, merged app-wide from
/// App.axaml). Deliberately not Avalonia's own Expander: its Fluent template
/// brings a tall header row with border and padding, which would eat
/// straight back into the space this whole feature exists to save.
/// </summary>
public class CollapsibleFilterBar : ContentControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleFilterBar, bool>(nameof(IsExpanded), defaultValue: true, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<CollapsibleFilterBar, string?>(nameof(Summary));

    public static readonly DirectProperty<CollapsibleFilterBar, string> SummaryDisplayTextProperty =
        AvaloniaProperty.RegisterDirect<CollapsibleFilterBar, string>(nameof(SummaryDisplayText), o => o.SummaryDisplayText);

    private const string NoFilterText = "Filters";

    private string _summaryDisplayText = NoFilterText;
    private Button? _toggleButton;
    private Button? _summaryButton;

    public CollapsibleFilterBar()
    {
        UpdateSummaryDisplay();
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>What's currently filtered, e.g. "Concerns me · Items"; empty/null while nothing is.</summary>
    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    /// <summary>What the collapsed bar actually shows: "Filters" alone, or "Filters: …" with the active ones.</summary>
    public string SummaryDisplayText
    {
        get => _summaryDisplayText;
        private set => SetAndRaise(SummaryDisplayTextProperty, ref _summaryDisplayText, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_toggleButton is not null)
        {
            _toggleButton.Click -= OnToggleClick;
        }

        if (_summaryButton is not null)
        {
            _summaryButton.Click -= OnSummaryClick;
        }

        _toggleButton = e.NameScope.Find<Button>("PART_ToggleButton");
        _summaryButton = e.NameScope.Find<Button>("PART_SummaryButton");

        if (_toggleButton is not null)
        {
            _toggleButton.Click += OnToggleClick;
        }

        if (_summaryButton is not null)
        {
            _summaryButton.Click += OnSummaryClick;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SummaryProperty)
        {
            UpdateSummaryDisplay();
        }
    }

    private void UpdateSummaryDisplay()
    {
        var isFiltering = !string.IsNullOrEmpty(Summary);
        SummaryDisplayText = isFiltering ? $"{NoFilterText}: {Summary}" : NoFilterText;
        // Styled in Styles/FilterStyles.axaml - highlighted like an active
        // filter button while something is filtered, dimmed otherwise.
        PseudoClasses.Set(":filtering", isFiltering);
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    private void OnSummaryClick(object? sender, RoutedEventArgs e) => IsExpanded = true;
}
