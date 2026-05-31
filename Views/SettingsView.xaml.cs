using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Mosaic.ViewModels;

namespace Mosaic.Views;

public partial class SettingsView : UserControl
{
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyBox.PasswordChanged += OnPasswordChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldVm)
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is SettingsViewModel newVm)
        {
            newVm.PropertyChanged += OnVmPropertyChanged;
            SyncPasswordFromVm(newVm);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ApiKey) && sender is SettingsViewModel vm)
            SyncPasswordFromVm(vm);
    }

    private void SyncPasswordFromVm(SettingsViewModel vm)
    {
        if (_syncing)
            return;
        _syncing = true;
        if (KeyBox.Password != (vm.ApiKey ?? string.Empty))
            KeyBox.Password = vm.ApiKey ?? string.Empty;
        _syncing = false;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || DataContext is not SettingsViewModel vm)
            return;
        _syncing = true;
        vm.ApiKey = KeyBox.Password;
        _syncing = false;
    }

    private void ShowKey_Toggled(object sender, RoutedEventArgs e)
    {
        var show = ShowKey.IsChecked == true;
        KeyText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        KeyBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (!show && DataContext is SettingsViewModel vm)
            SyncPasswordFromVm(vm);
    }
}
