namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// The transport security an operator may configure for SMTP submission.
///
/// <para>A deliberately narrower enum than MailKit's own <c>SecureSocketOptions</c>, which also
/// offers <c>Auto</c> and <c>StartTlsWhenAvailable</c>. Both of those can end up sending over an
/// unencrypted connection when the server does not advertise STARTTLS — a silent downgrade of a
/// message carrying a customer's token link. Neither is reachable from configuration here: an
/// operator picks encryption explicitly, or picks <see cref="None"/> explicitly.</para>
/// </summary>
public enum EmailSecurityMode
{
    /// <summary>
    /// Plain connection, no TLS. Present because a local relay or an in-process test listener has
    /// no certificate — never a sensible choice against a real provider.
    /// </summary>
    None = 0,

    /// <summary>Connect in the clear, then upgrade with STARTTLS. Required, not opportunistic.</summary>
    StartTls = 1,

    /// <summary>TLS from the first byte (the historical "SMTPS" submission port).</summary>
    SslOnConnect = 2,
}
