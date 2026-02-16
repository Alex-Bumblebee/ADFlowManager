using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using ADFlowManager.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ADFlowManager.UI.Views.Dialogs;

/// <summary>
/// Dialog de recherche de groupes avec autosuggest depuis le cache AD.
/// Permet de sélectionner plusieurs groupes avant validation.
/// </summary>
public partial class GroupSearchDialog : System.Windows.Window
{
    private List<Group> _allGroups = [];
    private readonly ObservableCollection<Group> _selectedGroups = [];

    public List<Group> SelectedGroups => _selectedGroups.ToList();

    public GroupSearchDialog(string title = "Rechercher des groupes")
    {
        InitializeComponent();

        PromptText.Text = title;
        SelectedList.ItemsSource = _selectedGroups;

        Loaded += async (_, _) =>
        {
            await LoadGroupsFromCacheAsync();
            SearchTextBox.Focus();
        };
    }

    private async Task LoadGroupsFromCacheAsync()
    {
        try
        {
            var cacheService = App.Services.GetRequiredService<ICacheService>();
            var cached = await cacheService.GetCachedGroupsAsync();
            if (cached != null)
            {
                _allGroups = cached;
            }
            else
            {
                var adService = App.Services.GetRequiredService<IActiveDirectoryService>();
                _allGroups = await adService.GetGroupsAsync();
            }
        }
        catch
        {
            _allGroups = [];
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

        var selectedNames = _selectedGroups.Select(g => g.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = _allGroups
            .Where(g => !selectedNames.Contains(g.GroupName) &&
                        (g.GroupName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                         g.Description.Contains(text, StringComparison.OrdinalIgnoreCase)))
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
        if (sender is FrameworkElement fe && fe.DataContext is Group group)
        {
            AddGroup(group);
        }
    }

    private void AddGroup(Group group)
    {
        if (_selectedGroups.Any(g => g.GroupName.Equals(group.GroupName, StringComparison.OrdinalIgnoreCase)))
            return;

        _selectedGroups.Add(group);
        UpdateUI();
        SearchTextBox.Text = "";
        SuggestPopup.IsOpen = false;
        SearchTextBox.Focus();
    }

    private void RemoveSelected_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string groupName)
        {
            var toRemove = _selectedGroups.FirstOrDefault(g => g.GroupName == groupName);
            if (toRemove != null)
            {
                _selectedGroups.Remove(toRemove);
                UpdateUI();
            }
        }
    }

    private void UpdateUI()
    {
        ValidateButton.Content = $"Valider ({_selectedGroups.Count})";
        ValidateButton.IsEnabled = _selectedGroups.Count > 0;
        SelectedLabel.Visibility = _selectedGroups.Count > 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_selectedGroups.Count == 0) return;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
