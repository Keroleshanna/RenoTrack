namespace RenoTrack.Infrastructure.Documents;

/// <summary>
/// A document cannot be generated because the deployment's own configuration is incomplete — today,
/// the company's legal identity (<c>CompanyLegalIdentity</c>, D110). Phase 14 Slice 3a, <b>D113</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A server defect, never a 409.</b> An Admin cannot fix a missing tax number from the screen
/// they are on; an operator has to supply it. So this derives directly from <see cref="Exception"/>
/// — never from <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>, which
/// the API maps to 409 and 400 (D59). It falls through to the unmapped arm: a 500 whose body carries
/// no detail, logged at <c>Error</c>. A test pins the base type, because "fixing" it into an
/// <see cref="InvalidOperationException"/> would quietly turn an operator's problem into a user's.
/// </para>
/// <para>
/// <b>The message names configuration keys and never their values</b> — it reaches the server log,
/// and a value would be the company's own data.
/// </para>
/// </remarks>
public sealed class DocumentConfigurationException : Exception
{
    public DocumentConfigurationException(string message)
        : base(message)
    {
    }
}
