using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// The password dialog shown when "Admin" is clicked while not logged in
/// (Admin-Funktionen.md in the feature-plan archive). Server answers are
/// shown right in the dialog instead of only as a chat line.
/// </summary>
public partial class AdminLoginViewModel : ViewModelBase
{
    private readonly AdminPanelViewModel _admin;

    public AdminLoginViewModel(AdminPanelViewModel admin)
    {
        _admin = admin;
    }

    public string Title => $"Admin login - {_admin.ServerName}";

    public string IntroText =>
        $"Log in as admin of {_admin.ServerName} to see every player and send items or locations. " +
        "This is the server's admin password (server_password), not the room password.";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>Shown after "Remote administration is disabled" - how to give the server an admin password.</summary>
    [ObservableProperty]
    private bool _showDisabledHelp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    private bool _isBusy;

    public bool CanLogin => !IsBusy;

    /// <summary>True once logged in - the view closes itself then.</summary>
    public async Task<bool> TryLoginAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _admin.LoginAsync(Password);
            if (result == AdminLoginResult.Success)
            {
                return true;
            }

            ErrorText = AdminPanelViewModel.DescribeLoginFailure(result);
            ShowDisabledHelp = result == AdminLoginResult.RemoteAdminDisabled;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
