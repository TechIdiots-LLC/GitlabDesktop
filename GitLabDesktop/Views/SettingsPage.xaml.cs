using GitLabDesktop.ViewModels;

namespace GitLabDesktop.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        ModalPageHelper.Attach(this, vm);
    }
}
