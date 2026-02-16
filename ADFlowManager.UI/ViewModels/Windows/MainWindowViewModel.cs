using System.Collections.ObjectModel;
using ADFlowManager.Core.Interfaces.Services;
using Wpf.Ui.Controls;

namespace ADFlowManager.UI.ViewModels.Windows
{
    /// <summary>
    /// ViewModel de la fenêtre principale. Configure la navigation et les menus.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "ADFlowManager";

        [ObservableProperty]
        private ObservableCollection<object> _menuItems = [];

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = [];

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = [];

        public MainWindowViewModel(ILocalizationService localization)
        {
            MenuItems = new ObservableCollection<object>
            {
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_Home"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                    TargetPageType = typeof(Views.Pages.DashboardPage)
                },
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_Users"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.People24 },
                    TargetPageType = typeof(Views.Pages.UsersPage)
                },
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_Groups"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.PeopleTeam24 },
                    TargetPageType = typeof(Views.Pages.GroupsPage)
                },
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_CreateUser"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.PersonAdd24 },
                    TargetPageType = typeof(Views.Pages.CreateUserPage)
                },
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_History"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.History24 },
                    TargetPageType = typeof(Views.Pages.HistoriquePage)
                },
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_Templates"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.DocumentCopy24 },
                    TargetPageType = typeof(Views.Pages.TemplatesPage)
                }
            };

            FooterMenuItems = new ObservableCollection<object>
            {
                new NavigationViewItem()
                {
                    Content = localization.GetString("Nav_Settings"),
                    Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                    TargetPageType = typeof(Views.Pages.SettingsPage)
                }
            };

            TrayMenuItems = new ObservableCollection<MenuItem>
            {
                new MenuItem { Header = localization.GetString("Nav_Home"), Tag = "tray_home" }
            };
        }
    }
}
