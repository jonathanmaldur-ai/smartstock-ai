namespace SmartStock.Domain.Auditing;

/// <summary>
/// Códigos das ações auditadas. Usar sempre estas constantes para manter o log pesquisável.
/// </summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string Logout = "auth.logout";
    public const string PasswordResetRequested = "auth.password.reset_requested";
    public const string PasswordDefined = "auth.password.defined";
    public const string PasswordDefineFailed = "auth.password.define_failed";

    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string UserActivated = "user.activated";
    public const string UserInviteResent = "user.invite_resent";

    public const string AdminSeeded = "system.admin_seeded";

    public const string StoreUpdated = "store.updated";
    public const string CategoryExclusionChanged = "category.exclusion_changed";
    public const string CategoryMerged = "category.merged";
    public const string ProductSeasonalChanged = "product.seasonal_changed";

    public const string ImportUploaded = "import.uploaded";
    public const string ImportRejected = "import.rejected";
    public const string ImportConfirmed = "import.confirmed";
    public const string ImportDiscarded = "import.discarded";

    public const string AnalysisGenerated = "analysis.generated";
    public const string AnalysisParametersUpdated = "analysis.parameters_updated";
    public const string SuggestionsApproved = "suggestion.approved";
    public const string SuggestionsRejected = "suggestion.rejected";
    public const string AlertsSeen = "alert.seen";
    public const string AlertsUnseen = "alert.unseen";
}
