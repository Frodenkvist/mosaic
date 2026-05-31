using System.Windows;
using Mosaic.Theming;
using Mosaic.ViewModels;

namespace Mosaic.Views;

public partial class AddGameWindow : MosaicWindow
{
    public AddGameWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameViewModel vm && !vm.IsValid)
        {
            MessageBox.Show(this, "Please choose an executable first.", "Add Game",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
