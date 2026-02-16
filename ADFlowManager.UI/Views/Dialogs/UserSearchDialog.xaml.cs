using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de recherche d'utilisateurs avec autosuggest depuis le cache AD.
/// Permet de sélectionner plusieurs utilisateurs avant validation.
/// </summary>
public partial class UserSearchDialog : System.Windows.Window
{
    private List<User> _allUsers = [];
    private readonly ObservableCollection<User> _selectedUsers = [];

    public List<User> SelectedUsers => _selectedUsers.ToList();

    public UserSearchDialog(string title = "Rechercher des utilisateurs")
    {
        InitializeComponent();

        PromptText.Text = title;
        SelectedList.ItemsSource = _selectedUsers;

        Loaded += async (_, _) =>
        {
            await LoadUsersFromCacheAsync();
            SearchTextBox.Focus();
        };
    }

    private async Task LoadUsersFromCacheAsync()
    {
        try
        {
            var cacheService = App.Services.GetRequiredService<ICacheService>();
            var cached = await cacheService.GetCachedUsersAsync();
            if (cached != null)
            {
                _allUsers = cached;
            }
            else
            {
                var adService = App.Services.GetRequiredService<IActiveDirectoryService>();
                _allUsers = await adService.GetUsersAsync();
            }
        }
        catch
        {
            _allUsers = [];
        }
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var text = SearchTextBox.Text?.Trim() ?? "";

        if (text.Length < 2)
        {
            SuggestPopup.IsOpen = false;
            return;
        }

        var selectedNames = _selectedUsers.Select(u => u.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = _allUsers
            .Where(u => !selectedNames.Contains(u.UserName) &&
                        (u.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                         u.UserName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                         u.Email.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Take(10)
            .ToList();

        if (filtered.Count > 0)
        {
            SuggestList.ItemsSource = filtered;
            SuggestPopup.IsOpen = true;
        }
        else
        {
            SuggestPopup.IsOpen = false;
        }
    }

    private void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            SuggestPopup.IsOpen = false;
        }
    }

    private void SuggestItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is User user)
        {
            AddUser(user);
        }
    }

    private void AddUser(User user)
    {
        if (_selectedUsers.Any(u => u.UserName.Equals(user.UserName, StringComparison.OrdinalIgnoreCase)))
            return;

        _selectedUsers.Add(user);
        UpdateUI();
        SearchTextBox.Text = "";
        SuggestPopup.IsOpen = false;
        SearchTextBox.Focus();
    }

    private void RemoveSelected_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string userName)
        {
            var toRemove = _selectedUsers.FirstOrDefault(u => u.UserName == userName);
            if (toRemove != null)
            {
                _selectedUsers.Remove(toRemove);
                UpdateUI();
            }
        }
    }

    private void UpdateUI()
    {
        ValidateButton.Content = $"Valider ({_selectedUsers.Count})";
        ValidateButton.IsEnabled = _selectedUsers.Count > 0;
        SelectedLabel.Visibility = _selectedUsers.Count > 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedUsers.Count == 0) return;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
