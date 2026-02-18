# Changelog

All notable changes to ADFlowManager will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.5-beta] - 2026-02-18

### Improvements/Fixed

- **Theme not respecting settings** (issue #3): Application now applies the theme from Settings on startup instead of following the Windows system theme. Defaults to Dark if no preference is saved
- **Included/Excluded OUs filter** (issue #1): OU filters in Settings now correctly apply to the user list. When Included OUs are configured, AD is scanned only on those specific OUs (targeted `PrincipalContext` per OU) instead of the full domain - significantly faster on large directories. Excluded OUs are filtered by DN on both AD results and cache reads. Cache is automatically invalidated and users reloaded when these filters change on save.
- **OU visibility** (issue #4): Organizational Units are now displayed as `Parent OU → OU Name` (breadcrumb path) in all OU pickers (Settings, Create User, Groups). The full DN is shown as a tooltip.
- **Cache TTL extended to 24h** (issue #2): Cache lifetime slider now goes up to 1440 min (24h) with snap-to-tick marks at 15, 30, 60, 120, 240, 480, 720 and 1440 min.

## [0.1.4-beta] - 2026-02-17

Correction for automatic update (release)

## [0.1.0-beta] - 2026-02-16

**⚠️ Beta Release** - This is a pre-release version for testing purposes. All planned features are complete, but the software may contain bugs.

### 🎉 First Public Beta Release!

ADFlowManager is a modern Active Directory management tool built with .NET 10 and WPF, designed to be significantly faster and more user-friendly than traditional PowerShell-based tools.

**Performance**: Significantly faster than PowerShell cmdlets thanks to native .NET APIs and intelligent caching system.
**Privacy First**: No telemetry, no data collection. Everything runs locally on your infrastructure.

---

### ✨ Features

#### 🔐 Active Directory Management

**User Management**
- Create, edit, and disable user accounts
- Detailed user information (5 tabs: General, Contact, Organization, Groups, History)
- Password reset with strength indicator and secure generation
- Intelligent user copying (copies organizational data and groups, skips personal information)
- Compare users and sync rights bidirectionally
- Copy rights between users (batch group assignments)

**Group Management**
- Browse all AD groups
- Add/remove group members in bulk
- Display group membership

#### 📋 Templates System

**User Templates**
- Create reusable user templates
- Store templates locally or on network share (multi-user collaboration)
- Apply templates during user creation
- Import/Export templates (JSON format)
- Network-shared templates for team consistency

#### 🌍 Internationalization

**Multi-language Support**
- French (Français) - Default
- English (English)
- Switchable language in Settings
- ⚠️ Note: Application restart required after language change

#### ⚡ Performance

**Smart Caching**
- SQLite-based cache for users and groups
- Significantly faster than direct AD queries
- Configurable TTL (Time To Live) (60-1440 minutes)
- Manual refresh on demand
- Automatic refresh when cache expires

**Native Performance**
- Significantly faster than PowerShell cmdlets
- Built with native C# .NET 10 APIs as much as possible
- Async/await throughout the application
- Minimizes PowerShell overhead

#### 📊 Audit & History

**Multi-user Audit System**
- Comprehensive action tracking (create, update, disable, enable, password reset)
- SQLite database (local or network shared)
- Filter by date, user, or action type
- Export audit logs to CSV
- User-specific history in UserDetailsWindow
- Multi-user support for team environments

#### 🎨 Interface

**Dashboard**
- Real-time statistics (users, groups, daily actions)
- Recent activity feed (last 10 actions)
- Quick actions (create user, manage templates, view history)

**Modern Design**
- Modern WPF-UI dark theme
- Responsive design
- Intuitive navigation

#### ⚙️ Settings & Configuration

**Comprehensive Settings Panel**
- **General**: Language, startup options
- **Active Directory**: Connection settings
- **Cache**: TTL configuration, manual refresh
- **Logs**: Level, retention policies
- **Audit**: Local/network database path
- **Templates**: Local/network storage path
- **About**: Version, license information

**Configuration Management**
- Export configuration to JSON
- Import configuration from JSON
- Portable settings across installations

#### 🔒 Security

**Credential Management**
- Secure credential storage (Windows Credential Manager)
- Optional credential persistence ("Remember me")
- Auto-login support for trusted environments

#### 🔄 Auto-Update

**Velopack Integration**
- Automatic update detection
- Delta updates (significant bandwidth savings)
- Silent background installation
- GitHub Releases integration
- Seamless version transitions

---

### 🛠️ Technical Details

- **Framework**: .NET 10 (Standard Term Support)
- **UI**: WPF with modern WPF-UI controls
- **Architecture**: Clean Architecture + MVVM pattern
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Logging**: Serilog (structured logging)
- **Database**: SQLite (Entity Framework Core)
- **AD Integration**: System.DirectoryServices.AccountManagement
- **Auto-update**: Velopack
- **UI Template**: WPF-UI (Wpf.Ui.Controls)
- **License**: GPLv3 (Open Source)

---

### 📋 Requirements

- **OS**: Windows 10 (version 1809+) or Windows 11
- **Runtime**: .NET 10 Desktop Runtime *(included in installer)*
- **Network**: Active Directory domain connection
- **Permissions**: Domain user account (admin rights recommended for full functionality)
- **Disk Space**: ~100 MB

---

### 🐛 Known Issues (Beta)

- ⚠️ Language change requires application restart
- ⚠️ Some UI labels may not be fully translated (~95% FR/EN coverage)
- ⚠️ Network templates require write permissions on shared folder
- ⚠️ First cache load may take time depending on AD size
- ⚠️ 7 warnings: NU1701 (CredentialManagement legacy package - functional, non-blocking)
- ⚠️ ~200 warnings: CA1416 (Windows-specific API calls - expected for Windows-only app)

---

### 📝 Beta Testing Feedback

I'm actively seeking feedback on:

- **🔍 Performance**: Is the speed improvement noticeable? Any bottlenecks?
- **🐛 Bugs**: Crashes, errors, unexpected behavior
- **🎨 UI/UX**: Confusing workflows, missing features, design issues
- **🌍 Localization**: Missing or incorrect translations
- **📦 Auto-update**: Does the update mechanism work smoothly?
- **📊 Features**: Which features do you use most? What's missing?

**Report issues**: [GitHub Issues](https://github.com/Alex-Bumblebee/ADFlowManager/issues)  
**Discussions**: [GitHub Discussions](https://github.com/Alex-Bumblebee/ADFlowManager/discussions)

---

### 🚀 Roadmap to v1.0.0

Post-beta priorities:
- ✅ Fix all critical bugs reported by beta testers
- ✅ Complete remaining translations (100% coverage)
- ✅ Performance optimizations based on feedback
- ✅ Documentation improvements (user guide, admin guide)
- ✅ Code review and quality polish

**v1.0.0 will be the first production-ready stable release.**

---

### 📦 Installation

1. Download `ADFlowManager-Setup.exe` from [Releases](https://github.com/Alex-Bumblebee/ADFlowManager/releases/tag/v0.1.0-beta)
2. Run the installer (admin rights not required)
3. Launch ADFlowManager
4. Connect with your Active Directory credentials
5. Start managing your AD environment!

**Note**: .NET 10 Desktop Runtime is bundled with the installer. No manual installation required.

---

### 🔄 Updating from Beta

Future updates are automatic via Velopack:
- Beta → Beta: `v0.1.0-beta` → `v0.1.1-beta` → `v0.1.2-beta`
- Beta → Stable: `v0.1.x-beta` → `v1.0.0`

The application will notify you when updates are available and install them in the background.

---

## [Unreleased]

### Planned for v1.0.0 (Stable)
- Complete internationalization coverage (100% FR/EN)
- All critical beta bugs resolved
- Performance benchmarks published
- Complete documentation suite
- Community feedback integrated

### Planned for v1.1.0+
- **Export/Import**: Full CSV/Excel support for users and groups
- **Advanced Search**: Combined filters with AND/OR logic
- **Custom Reports**: Configurable report templates with scheduling
- **Theme Options**: Dark/Light theme toggle
- **Keyboard Shortcuts**: Enhanced productivity shortcuts
- **OU Management**: Create, move, and manage Organizational Units
- **Naming Policies**: Automatic field modification based on your naming policies

For complete future roadmap, see [ROADMAP.md](ROADMAP.md). Will be available in v1.0.0

---

## Development & Contributing

**Repository**: [GitHub - ADFlowManager](https://github.com/Alex-Bumblebee/ADFlowManager)  
**License**: [GPLv3](https://github.com/Alex-Bumblebee/ADFlowManager/blob/main/LICENSE)  
**Author**: Alex Bumblebee  
**UI Template**: [WPF-UI](https://github.com/lepoco/wpfui)

### Contributing

Beta testing contributions are welcome:
- **Bug Reports**: [Open an issue](https://github.com/Alex-Bumblebee/ADFlowManager/issues/new)
- **Feature Requests**: [Start a discussion](https://github.com/Alex-Bumblebee/ADFlowManager/discussions)
- **Translations**: i18n documentation will be available in v1.0.0
- **Code**: Fork, improve, and submit pull requests

---

**Thank you for testing ADFlowManager v0.1.0-beta!** 🙏

Your feedback would really make me happy and help make v1.0.0 production-ready! 🚀

---

*Built with ❤️ for the Active Directory admin community*