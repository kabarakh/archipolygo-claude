using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TestHarness.AddSlotPickerPrototype;

public partial class AddSlotPickerPrototypeWindow : Window
{
    public AddSlotPickerPrototypeWindow()
    {
        InitializeComponent();
    }

    private AddSlotPickerPrototypeViewModel ViewModel => (AddSlotPickerPrototypeViewModel)DataContext!;

    private void OnApplyClick(object? sender, RoutedEventArgs e) => Close(ViewModel.BuildResult());

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
