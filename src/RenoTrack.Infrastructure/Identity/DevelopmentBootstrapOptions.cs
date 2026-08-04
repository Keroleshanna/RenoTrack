using Microsoft.Extensions.Configuration;

namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// One account <see cref="DevelopmentBootstrap"/> may provision. Deliberately a plain carrier with
/// no behaviour.
///
/// <para><b>Which accounts exist is fixed at compile time, not configurable.</b>
/// <see cref="DevelopmentBootstrapOptions.FromConfiguration"/> constructs exactly two, and adding a
/// third is a code change there — necessarily so, because <paramref name="Role"/> must never come
/// from configuration. What being a data carrier buys is confined to the consumer:
/// <see cref="DevelopmentBootstrap"/> iterates this list instead of running a hand-written block per
/// account, so adding one is a single added line in <c>FromConfiguration</c> and <b>no change at all</b>
/// to the seeder — which is where a duplicated per-account sequence would otherwise drift.</para>
/// </summary>
/// <param name="Role">
/// Fixed per account by <see cref="DevelopmentBootstrapOptions.FromConfiguration"/>, never read from
/// configuration. Letting configuration choose the role would turn a development convenience into a
/// way to mint an arbitrary privileged account from a settings file, which is exactly the surface
/// this design is trying not to create.
/// </param>
/// <param name="SectionName">
/// The fully-qualified configuration path this account was read from (e.g.
/// <c>DevelopmentBootstrap:Admin</c>), carried so every validation failure can name the exact key at
/// fault rather than describing it.
/// </param>
public sealed record DevelopmentBootstrapAccount(
    string Email,
    string? Password,
    string Name,
    string Role,
    string SectionName)
{
    /// <summary>The key an operator must set to make this account usable. Never logged with a value.</summary>
    public string PasswordKey => $"{SectionName}:{nameof(Password)}";

    /// <summary>
    /// Named in the duplicate-address failure, so an operator is told which two keys collide rather
    /// than being left to work out which accounts share the address.
    /// </summary>
    public string EmailKey => $"{SectionName}:{nameof(Email)}";
}

/// <summary>
/// Binds the <c>DevelopmentBootstrap</c> configuration section — whether development accounts are
/// provisioned at startup, and with what credentials (D64).
/// </summary>
/// <remarks>
/// <para>
/// <b>Passwords have no default and are never compiled in.</b> The recommended source is
/// <c>dotnet user-secrets</c>, for a reason beyond tidiness: <c>WebApplication.CreateBuilder</c>
/// adds the user-secrets provider <em>only</em> when the environment is Development, so in
/// Production the credential cannot arrive by that route at all — a second gate, independent of
/// <see cref="DevelopmentBootstrap"/>'s own environment guard. <c>appsettings.Development.json</c>
/// (gitignored) and environment variables remain supported; standard configuration precedence
/// applies, so user secrets override the file and environment variables override both.
/// </para>
/// <para>
/// Parsed by hand rather than through <c>IConfiguration.Get&lt;T&gt;()</c>, for the same reason
/// <see cref="Persistence.DatabaseInitializationOptions"/> is: this setting decides whether startup
/// mints a privileged account, so a typo — <c>"yes"</c>, <c>"1 "</c>, <c>"True!"</c> — must produce
/// an unmistakable message rather than binding silently to <see langword="false"/> and leaving an
/// operator wondering why nothing was created.
/// </para>
/// </remarks>
public sealed class DevelopmentBootstrapOptions
{
    public const string SectionName = "DevelopmentBootstrap";

    /// <summary>The fully-qualified key, used verbatim in every error message.</summary>
    public const string EnabledKey = $"{SectionName}:{nameof(Enabled)}";

    private const string AdminSectionName = $"{SectionName}:Admin";
    private const string InspectorSectionName = $"{SectionName}:Inspector";

    /// <summary>
    /// <see langword="false"/> when the key is absent — the same fail-safe default as
    /// <c>Database:Mode</c> ⇒ <c>Verify</c>. A deployment that configures nothing provisions nothing.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Exactly two accounts: one Admin and one Inspector. An Admin alone cannot exercise a single
    /// ownership-scoped (<c>S</c>) path in <c>PermissionMatrix.md</c>, so the first thing anyone
    /// would do is hand-create an Inspector — the precise manual step this exists to remove. Nothing
    /// further is provisioned (no second Inspector, no deactivated or role-less account): those exist
    /// in <c>RenoTrack.Api.Tests</c> because tests must <em>prove</em> edge behaviour, whereas a
    /// developer driving the UI does not need them, and every extra account is more privileged
    /// surface behind the same guard. Growth-on-demand (CLAUDE.md §4) applied to seed data.
    /// </summary>
    public IReadOnlyList<DevelopmentBootstrapAccount> Accounts { get; init; } = [];

    public static DevelopmentBootstrapOptions FromConfiguration(IConfiguration configuration)
    {
        return new DevelopmentBootstrapOptions
        {
            Enabled = ReadEnabled(configuration),
            Accounts =
            [
                ReadAccount(configuration, AdminSectionName, IdentityRoleSeeder.AdminRole,
                    defaultEmail: "dev-admin@renotrack.test", defaultName: "Development Admin"),
                ReadAccount(configuration, InspectorSectionName, IdentityRoleSeeder.InspectorRole,
                    defaultEmail: "dev-inspector@renotrack.test", defaultName: "Development Inspector"),
            ],
        };
    }

    private static bool ReadEnabled(IConfiguration configuration)
    {
        var configured = configuration[EnabledKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        if (!bool.TryParse(configured, out var enabled))
        {
            throw new InvalidOperationException(
                $"Configuration '{EnabledKey}' has value '{configured}', which is not a valid boolean. " +
                "Allowed values: true, false.");
        }

        return enabled;
    }

    /// <summary>
    /// Email and display name fall back to fixed defaults; the password deliberately does not, so an
    /// enabled bootstrap with no configured password fails loudly instead of inventing a credential.
    /// The default addresses use the RFC 2606-reserved <c>.test</c> TLD, which can never resolve, and
    /// carry a <c>dev-</c> prefix specifically so they cannot collide with the differently-purposed
    /// accounts <c>RenoTrackApiFactory</c> seeds — two account sets sharing an address would be
    /// indistinguishable when reading either one.
    /// </summary>
    private static DevelopmentBootstrapAccount ReadAccount(
        IConfiguration configuration,
        string sectionName,
        string role,
        string defaultEmail,
        string defaultName)
    {
        var section = configuration.GetSection(sectionName);

        var email = section[nameof(DevelopmentBootstrapAccount.Email)];
        var name = section[nameof(DevelopmentBootstrapAccount.Name)];

        return new DevelopmentBootstrapAccount(
            Email: string.IsNullOrWhiteSpace(email) ? defaultEmail : email,
            Password: section[nameof(DevelopmentBootstrapAccount.Password)],
            Name: string.IsNullOrWhiteSpace(name) ? defaultName : name,
            Role: role,
            SectionName: sectionName);
    }

    /// <summary>
    /// Validates the accounts, and is called by <see cref="DevelopmentBootstrap"/> only <b>after</b>
    /// its environment guard has passed — deliberately not eagerly at composition time like
    /// <c>JwtOptions.Validate()</c>. A Production host configured with
    /// <c>DevelopmentBootstrap:Enabled=true</c> and no password must be told that the whole feature
    /// is refused in Production, which is the actionable problem; being told to supply a password it
    /// must never supply would point the operator in precisely the wrong direction.
    /// </summary>
    /// <remarks>
    /// A blank or absent email or display name cannot fail: each has already been replaced by its
    /// default in <see cref="FromConfiguration"/>, so a check for one here would be unreachable code
    /// asserting something the parser guarantees. What *can* fail is two accounts pointing at the
    /// <em>same</em> address, which no per-value check would catch.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Two accounts share an email address, or an account has no configured password.
    /// </exception>
    public void Validate()
    {
        // Checked before passwords because it is a defect in the shape of the account set as a whole,
        // rather than one account missing one value — reporting the structural problem first reads
        // better when a configuration has both.
        EnsureAccountAddressesAreDistinct();

        foreach (var account in Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Password))
            {
                throw new InvalidOperationException(
                    $"Configuration '{account.PasswordKey}' is not set, but '{EnabledKey}' is true. " +
                    "Development account passwords have no default and are never compiled in. Set one with: " +
                    $"dotnet user-secrets set \"{account.PasswordKey}\" \"<password>\" --project src/RenoTrack.Api");
            }
        }
    }

    /// <summary>
    /// Two accounts configured with the same address is a configuration error that would otherwise
    /// fail <em>silently and misleadingly</em>. Provisioning is create-only by design, so the first
    /// account would be created, the second would find that address already taken, leave it untouched
    /// as instructed, and log "already exists and was left untouched" — a message that reads as
    /// benign. The observable result is a single account holding only the first role, with nothing in
    /// the log suggesting anything went wrong.
    ///
    /// <para>Compared case-insensitively, matching Identity's own normalization: <c>UserManager</c>
    /// stores an upper-invariant <c>NormalizedEmail</c>, so two addresses differing only in case are
    /// one account to it and must be one collision here.</para>
    /// </summary>
    private void EnsureAccountAddressesAreDistinct()
    {
        var collision = Accounts
            .GroupBy(account => account.Email, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (collision is null)
        {
            return;
        }

        // Both keys are named, so an operator does not have to work out which accounts collided.
        var keys = string.Join(" and ", collision.Select(account => $"'{account.EmailKey}'"));

        throw new InvalidOperationException(
            $"Configuration {keys} are both set to '{collision.Key}'. Each development account must have " +
            "its own address: provisioning is create-only, so the second account would find the first " +
            "already present and leave it untouched, silently producing one account that holds only the " +
            "first role. Addresses are compared case-insensitively, matching how Identity normalizes them.");
    }
}
