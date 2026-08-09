using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// A minimal SMTP server that runs inside the test process, so <see cref="SmtpEmailSenderTests"/>
/// exercises the real MailKit client over a real socket.
///
/// <para><b>Why this and not a container or a hosted sink.</b> Docker is not installed on this
/// machine and neither CI job provides an SMTP server, so smtp4dev/MailHog would mean either a
/// skipped test in CI or an environment prerequisite that D56 exists to avoid. A mocked
/// <c>SmtpClient</c> is not an option either — <c>CLAUDE.md</c> §14 forbids mocking frameworks, and
/// mocking the transport would verify nothing about the transport. This speaks just enough of
/// RFC 5321 to complete a session, and everything above the socket is genuinely MailKit's code.</para>
///
/// <para>Deliberately not a general-purpose server: no TLS, no pipelining, no SIZE, and it accepts
/// one connection at a time.</para>
/// </summary>
public sealed class InProcessSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _acceptLoop;
    private readonly List<string> _messages = [];
    private readonly List<string> _commands = [];
    private readonly Lock _sync = new();

    public InProcessSmtpServer(bool advertiseAuthentication = false)
    {
        AdvertisesAuthentication = advertiseAuthentication;
        _listener = new TcpListener(IPAddress.Loopback, port: 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(() => AcceptAsync(_cancellation.Token));
    }

    public int Port { get; }

    public bool AdvertisesAuthentication { get; }

    /// <summary>Raw DATA payloads received, in arrival order.</summary>
    public IReadOnlyList<string> Messages
    {
        get { lock (_sync) { return [.. _messages]; } }
    }

    /// <summary>Every command line received, so a test can assert that AUTH was or was not attempted.</summary>
    public IReadOnlyList<string> Commands
    {
        get { lock (_sync) { return [.. _commands]; } }
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            using (client)
            {
                try
                {
                    await ServeAsync(client, cancellationToken);
                }
                catch (IOException)
                {
                    // A client that disconnects mid-session is a scenario under test, not a fault.
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        await writer.WriteLineAsync("220 localhost ESMTP InProcessSmtpServer");

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lock (_sync)
            {
                _commands.Add(line);
            }

            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
            {
                if (AdvertisesAuthentication)
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 AUTH PLAIN LOGIN");
                }
                else
                {
                    await writer.WriteLineAsync("250 localhost");
                }
            }
            else if (line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
            {
                // Accept whatever is offered; these tests care that authentication was attempted,
                // not that a particular mechanism succeeded.
                await writer.WriteLineAsync("235 2.7.0 Authentication successful");
            }
            else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 2.1.0 Ok");
            }
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");

                var payload = new StringBuilder();

                while (await reader.ReadLineAsync(cancellationToken) is { } dataLine && dataLine != ".")
                {
                    payload.AppendLine(dataLine);
                }

                lock (_sync)
                {
                    _messages.Add(payload.ToString());
                }

                await writer.WriteLineAsync("250 2.0.0 Ok: queued");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 2.0.0 Bye");
                return;
            }
            else
            {
                await writer.WriteLineAsync("250 2.0.0 Ok");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();

        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation is how the loop is stopped.
        }

        _cancellation.Dispose();
    }
}
