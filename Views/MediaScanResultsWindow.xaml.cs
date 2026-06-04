using System.Windows;
using Mosaic.Theming;

namespace Mosaic.Views;

public partial class MediaScanResultsWindow : MosaicWindow
{
    public MediaScanResultsWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
