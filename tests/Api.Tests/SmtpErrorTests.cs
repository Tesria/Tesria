using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Tesria.Api.Domain;
using Tesria.Api.Infrastructure.Email;
using Xunit;

namespace Tesria.Api.Tests;

/// <summary>
/// The mail server's failures in sentences (t2-012), from the exceptions
/// MailKit really throws, against a few lines of fake SMTP server.
/// </summary>
public class SmtpErrorTests
{
    /// <summary>A plain-text SMTP server that greets, answers EHLO without STARTTLS, and refuses AUTH.</summary>
    private static (int Port, Task Serving, CancellationTokenSource Stop) PlainServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var stop = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                    await writer.WriteLineAsync("220 fake ESMTP");
                    string? line;
                    while ((line = await reader.ReadLineAsync(stop.Token)) is not null)
                    {
                        if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("250-fake");
                            await writer.WriteLineAsync("250 AUTH PLAIN LOGIN");
                        }
                        else if (line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
                            await writer.WriteLineAsync("535 5.7.8 Authentication credentials invalid");
                        else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                        {
                            await writer.WriteLineAsync("221 bye");
                            break;
                        }
                        else await writer.WriteLineAsync("250 ok");
                    }
                }
            }
            catch (Exception) { /* stopped, or the client hung up mid-handshake */ }
            finally { listener.Stop(); }
        });
        return (port, serving, stop);
    }

    private static async Task<Exception> FailureAsync(string host, int port, SecureSocketOptions security, bool signIn = false)
    {
        using var client = new SmtpClient { Timeout = 5000 };
        try
        {
            await client.ConnectAsync(host, port, security);
            if (signIn) await client.AuthenticateAsync("someone", "wrong");
        }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("expected a failure");
    }

    [Fact]
    public async Task Ssl_on_a_plain_port_says_to_check_the_port_and_encryption_together()
    {
        var (port, _, stop) = PlainServer();
        var ex = await FailureAsync("127.0.0.1", port, SecureSocketOptions.SslOnConnect);
        stop.Cancel();

        var said = SmtpErrors.Explain(ex, "127.0.0.1", port, SmtpTlsMode.SslOnConnect);
        Assert.Contains("did not start an encrypted connection", said);
        Assert.DoesNotContain("FAQ", said);
    }

    [Fact]
    public async Task Starttls_where_there_is_none_says_so()
    {
        var (port, _, stop) = PlainServer();
        var ex = await FailureAsync("127.0.0.1", port, SecureSocketOptions.StartTls);
        stop.Cancel();

        Assert.Contains("does not offer STARTTLS", SmtpErrors.Explain(ex, "127.0.0.1", port, SmtpTlsMode.StartTls));
    }

    [Fact]
    public async Task A_refused_password_says_to_check_it()
    {
        var (port, _, stop) = PlainServer();
        var ex = await FailureAsync("127.0.0.1", port, SecureSocketOptions.None, signIn: true);
        stop.Cancel();

        Assert.Contains("refused the user name and password", SmtpErrors.Explain(ex, "127.0.0.1", port, SmtpTlsMode.None));
    }

    [Fact]
    public async Task A_closed_port_and_an_unknown_host_are_named()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var closed = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var refused = await FailureAsync("127.0.0.1", closed, SecureSocketOptions.None);
        Assert.Contains($"refused the connection on port {closed}", SmtpErrors.Explain(refused, "127.0.0.1", closed, SmtpTlsMode.None));

        var unknown = await FailureAsync("no-such-host.invalid", 25, SecureSocketOptions.None);
        Assert.Equal("No mail server called no-such-host.invalid could be found. Check the host name.",
            SmtpErrors.Explain(unknown, "no-such-host.invalid", 25, SmtpTlsMode.None));
    }
}
