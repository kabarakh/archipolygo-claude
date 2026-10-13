using Avalonia.Controls;

namespace Archipolygo.Views;

/// <summary>The four item-class checkboxes as one "Classes" flyout button; DataContext is the host's <see cref="ViewModels.IEventItemClassFilter"/>.</summary>
public partial class ItemClassFilterButton : UserControl
{
    public ItemClassFilterButton()
    {
        InitializeComponent();
    }
}
