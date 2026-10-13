using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

/// <summary>
/// A filter row that collapses to a one-line summary of the active filters. Deliberately not Avalonia's
/// Expander: its Fluent template's tall header row would eat the space this exists to save.
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
        PseudoClasses.Set(":filtering", isFiltering);
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    private void OnSummaryClick(object? sender, RoutedEventArgs e) => IsExpanded = true;
}
