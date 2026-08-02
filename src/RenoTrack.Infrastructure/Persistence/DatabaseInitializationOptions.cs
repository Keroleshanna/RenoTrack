using Microsoft.Extensions.Configuration;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// What the application does about the database when it starts (D63).
/// </summary>
public enum DatabaseInitializationMode
{
    /// <summary>
    /// Read-only readiness check: the migration history must match the migrations this build knows
    /// about, and the required Identity roles must exist. Never writes, so the runtime login needs
    /// no DDL permission. <b>This is the default when nothing is configured</b>, so an omitted or
    /// mistyped-then-corrected setting fails safe rather than silently mutating a database.
    /// </summary>
    Verify = 0,

    /// <summary>
    /// Apply migrations, seed the role reference data, then run the same verification. A
    /// development convenience, <b>refused outright in Production</b> — see
    /// <see cref="DatabaseInitializer"/>.
    /// </summary>
    Migrate = 1,
}

/// <summary>
/// Binds <c>Database:Mode</c>. Deliberately parsed by hand rather than through
/// <c>IConfiguration.Get&lt;T&gt;()</c>: the binder's failure for an unrecognised enum value names
/// the target type rather than the allowed values, and this setting decides whether startup is
/// permitted to write to the database — a typo in it must produce an unmistakable message, not a
/// silent fallback to the default.
/// </summary>
/// <remarks>
/// There is deliberately <b>no "None"/"Skip" mode.</b> It was considered and rejected: every
/// environment that could plausibly want one — including a DBA-managed database the application
/// must not touch — is better served by <see cref="DatabaseInitializationMode.Verify"/>, which is
/// already read-only. An escape hatch would exist only to disable the check that catches the exact
/// failure this design was built for. If a real environment is ever found that genuinely cannot
/// perform a read-only verification, that evidence should reopen this decision rather than a mode
/// being added pre-emptively.
/// </remarks>
public sealed class DatabaseInitializationOptions
{
    public const string SectionName = "Database";

    /// <summary>The fully-qualified configuration key, used verbatim in every error message.</summary>
    public const string ModeKey = $"{SectionName}:{nameof(Mode)}";

    public DatabaseInitializationMode Mode { get; init; } = DatabaseInitializationMode.Verify;

    /// <summary>
    /// Reads and validates the mode eagerly, at composition time — the same fail-fast shape as
    /// <c>JwtOptions.Validate()</c> and the connection-string check.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured value is not a recognised mode.</exception>
    public static DatabaseInitializationOptions FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration[ModeKey];

        // Absent means Verify. This is the safe default by design (D63): a deployment that forgets
        // to configure anything gets the read-only path, never the writing one.
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new DatabaseInitializationOptions();
        }

        if (!Enum.TryParse<DatabaseInitializationMode>(configured, ignoreCase: true, out var mode)
            || !Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                $"Configuration '{ModeKey}' has value '{configured}', which is not a valid database initialization mode. " +
                $"Allowed values: {string.Join(", ", Enum.GetNames<DatabaseInitializationMode>())}.");
        }

        return new DatabaseInitializationOptions { Mode = mode };
    }
}
