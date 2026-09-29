using Npgsql;

namespace Tesria.Api.Infrastructure;

/// <summary>
/// Ends the process with an ordinary exit when an exception goes unhandled,
/// instead of letting the runtime abort it (0.8.1 QA, T1-036).
///
/// The runtime ends a crashed process by raising SIGABRT. In a container the
/// app is process 1, and Linux does not deliver a signal to process 1 that
/// it has no handler for, so the abort never landed: the process spun at
/// full CPU with nothing listening, "running" as far as Docker could tell,
/// and <c>restart: unless-stopped</c> never restarted it. A failed start (the
/// database not up yet after a reboot, a migration still pending, a
/// database password that does not match) left the wiki down until someone
/// restarted it by hand. An exit lets Docker start it again, and it comes
/// back by itself once the database does. (Compose also runs the app under
/// an init process now; this covers a container started any other way.)
/// </summary>
public static class ExitOnCrash
{
    public const int ExitCode = 1;

    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                Console.Error.WriteLine($"Unhandled exception. {e.ExceptionObject}");
                Console.Error.WriteLine(Explain(e.ExceptionObject as Exception));
                Console.Error.Flush();
            }
            catch
            {
                // Nothing to be done about a console that cannot be written;
                // exiting still matters more.
            }
            Environment.Exit(ExitCode);
        };
    }

    /// <summary>The line after the stack trace: what happens next, and for the usual causes, what to check.</summary>
    public static string Explain(Exception? ex)
    {
        const string next = "Tesria stopped. Docker starts it again (restart: unless-stopped), so it recovers by itself once the cause is gone.";
        return ex?.GetBaseException() switch
        {
            PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } =>
                next + " The database refused the app's password: if APP_DB_PASSWORD was just changed, the migrate service "
                + "gives the database the new one within seconds (docker compose logs migrate).",
            NpgsqlException or System.Net.Sockets.SocketException or TimeoutException =>
                next + " The database could not be reached: docker compose ps db shows whether it is running.",
            _ => next,
        };
    }
}
