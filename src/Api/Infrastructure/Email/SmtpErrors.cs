using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Tesria.Api.Infrastructure.Email;

/// <summary>
/// What went wrong talking to the mail server, in a sentence that points at
/// the fix (t2-012). The library's own text reached the settings page as it
/// was: a ten-line paragraph ending in a link to MailKit's FAQ for an
/// encryption mismatch, and the resolver's "Name or service not known" for a
/// mistyped host. The raw text stays in the server log, where the sender
/// writes the exception.
/// </summary>
public static class SmtpErrors
{
    public static string Explain(Exception ex, string host, int port, Domain.SmtpTlsMode tls)
    {
        var socket = Find<SocketException>(ex);
        if (socket is not null)
        {
            switch (socket.SocketErrorCode)
            {
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                    return $"No mail server called {host} could be found. Check the host name.";
                case SocketError.ConnectionRefused:
                    return $"{host} refused the connection on port {port}. Check the port, and that the mail server is running.";
                case SocketError.TimedOut:
                    return TimedOut(host, port);
                case SocketError.NetworkUnreachable:
                case SocketError.HostUnreachable:
                    return $"{host} cannot be reached from this server. Check the host name, and that this server can reach it.";
            }
        }

        switch (ex)
        {
            case SslHandshakeException handshake:
                // A certificate came back, so the connection was encrypted and
                // the certificate is what failed; otherwise the server did not
                // speak TLS on that port at all.
                if (handshake.ServerCertificate is not null)
                    return $"The mail server's certificate for {host} is not trusted (it may be self-signed, expired, or for another name). "
                         + "Use the host name the certificate is for.";
                return tls == Domain.SmtpTlsMode.SslOnConnect
                    ? $"The mail server did not start an encrypted connection on port {port}. Check the port and encryption together: "
                      + "465 goes with SSL on Connect, and 587 with STARTTLS."
                    : $"The encrypted connection to {host} could not be set up. Check the port and encryption together: "
                      + "465 goes with SSL on Connect, and 587 with STARTTLS.";
            case NotSupportedException when ex.Message.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase):
                return $"The mail server does not offer STARTTLS on port {port}. Check the port and encryption together: "
                     + "465 goes with SSL on Connect, 587 with STARTTLS, and a local relay often with None.";
            case AuthenticationException auth:
                // The server's reply is kept: providers put the reason there
                // ("Username and Password not accepted", "SmtpClientAuthentication
                // is disabled"), and the docs' troubleshooting quotes it.
                return "The mail server refused the user name and password. Check them; some providers want an app password "
                     + $"rather than the account's own.{Quoted(auth.Message)}";
            case ServiceNotAuthenticatedException:
                return "The mail server wants a sign-in before it sends: fill in the user name and password.";
            case SmtpCommandException command:
                return command.ErrorCode switch
                {
                    SmtpErrorCode.SenderNotAccepted =>
                        $"The mail server refused the From address ({ServerSaid(command)}). Use an address this account may send as.",
                    SmtpErrorCode.RecipientNotAccepted =>
                        $"The mail server refused the recipient's address ({ServerSaid(command)}).",
                    _ when (int)command.StatusCode == 530 =>
                        "The mail server wants a sign-in before it sends: fill in the user name and password.",
                    _ => $"The mail server refused the message ({ServerSaid(command)}).",
                };
            case SmtpProtocolException:
                return $"What answered on port {port} did not speak SMTP as expected. Check the port and encryption together: "
                     + "465 goes with SSL on Connect, and 587 with STARTTLS.";
            case TimeoutException:
                return TimedOut(host, port);
        }

        if (Find<TimeoutException>(ex) is not null) return TimedOut(host, port);
        if (Find<IOException>(ex) is not null)
            return $"The connection to {host} on port {port} was cut off. Check the port and encryption together: "
                 + "465 goes with SSL on Connect, and 587 with STARTTLS.";
        return "The mail server could not be reached or did not take the message. The server log has the details.";
    }

    private static string TimedOut(string host, int port) =>
        $"{host} did not answer on port {port} in time. Check the host and port, and that no firewall is in the way.";

    /// <summary>The mail server's own reply, which is written for people: "550 5.7.1 Relaying denied", say.</summary>
    private static string ServerSaid(SmtpCommandException command)
    {
        var text = command.Message.Trim();
        if (text.Length > 200) text = text[..200] + "…";
        return $"it said {(int)command.StatusCode}: {text}";
    }

    private static string Quoted(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length > 200) text = text[..200] + "…";
        return $" The server said: “{text}”";
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is T match) return match;
        return null;
    }
}
