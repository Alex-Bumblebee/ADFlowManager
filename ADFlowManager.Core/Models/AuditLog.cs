namespace ADFlowManager.Core.Models;

/// <summary>
/// Entrée log audit action AD.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Username { get; set; } = "";
    public string ActionType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityDisplayName { get; set; } = "";
    public string Details { get; set; } = "{}";
    public string Result { get; set; } = "Success";
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Types actions audit.
/// </summary>
public static class AuditActionType
{
    public const string CreateUser = "CreateUser";
    public const string UpdateUser = "UpdateUser";
    public const string DisableUser = "DisableUser";
    public const string EnableUser = "EnableUser";
    public const string ResetPassword = "ResetPassword";
    public const string AddUserToGroup = "AddUserToGroup";
    public const string RemoveUserFromGroup = "RemoveUserFromGroup";
    public const string CreateGroup = "CreateGroup";
    public const string Login = "Login";
}

/// <summary>
/// Types entités audit.
/// </summary>
public static class AuditEntityType
{
    public const string User = "User";
    public const string Group = "Group";
    public const string System = "System";
}
