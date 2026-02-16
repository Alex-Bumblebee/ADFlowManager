using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using ADFlowManager.UI.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADFlowManager.UI.Views.Pages;

/// <summary>
/// Convertisseur string non-vide → bool pour les indicateurs de validation.
/// </summary>
public class NotEmptyConverter : IValueConverter
{
    public static readonly NotEmptyConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Page de création d'un nouvel utilisateur Active Directory.
/// </summary>
public partial class CreateUserPage : INavigableView<CreateUserViewModel>
{
    public CreateUserViewModel ViewModel { get; }
    private int _suggestIndex = -1;

    public CreateUserPage(CreateUserViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    /// <summary>
    /// Gère la navigation clavier dans le dropdown AutoSuggest (flèches + Entrée + Escape).
    /// </summary>
    private void UserSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ViewModel.IsUserSearchDropdownOpen || ViewModel.FilteredUsers.Count == 0)
        {
            _suggestIndex = -1;
            return;
        }

        var count = ViewModel.FilteredUsers.Count;

        switch (e.Key)
        {
            case Key.Down:
                _suggestIndex = (_suggestIndex + 1) % count;
                HighlightSuggestItem(_suggestIndex);
                e.Handled = true;
                break;

            case Key.Up:
                _suggestIndex = _suggestIndex <= 0 ? count - 1 : _suggestIndex - 1;
                HighlightSuggestItem(_suggestIndex);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_suggestIndex >= 0 && _suggestIndex < count)
                {
                    ViewModel.SelectUserCommand.Execute(ViewModel.FilteredUsers[_suggestIndex]);
                    _suggestIndex = -1;
                }
                e.Handled = true;
                break;

            case Key.Escape:
                ViewModel.IsUserSearchDropdownOpen = false;
                _suggestIndex = -1;
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Met en surbrillance l'item à l'index donné dans le dropdown.
    /// </summary>
    private void HighlightSuggestItem(int index)
    {
        var container = UserSuggestList.ItemContainerGenerator.ContainerFromIndex(index);
        if (container is System.Windows.FrameworkElement element)
        {
            element.BringIntoView();
        }
    }
}
