using System.Collections.Concurrent;

namespace Tesria.Api.Features.Export;

/// <summary>
/// How many packs one person may import in an hour (dev-plan 8.5), counted
/// so that a refused import does not use the allowance up (t6-016).
///
/// <para>This was a sliding-window rate limit on the endpoint, which counted
/// every request: a person who got a taken key, a bad key and a wrong file
/// had spent most of their hour without importing anything, and the
/// refusal said "Wait a minute" (the limiter knew no better) when the real
/// wait was up to an hour. Now only an import that went in counts towards
/// the ten, and the refusal says when the next one can go in.</para>
///
/// <para>Attempts are still bounded, more loosely, because each one reads up
/// to 500 MB of somebody's zip: thirty in an hour is far past anyone fixing
/// a pack by hand. Kept in memory, like the rate limiters it replaces: a
/// restart forgets it, which costs nothing.</para>
/// </summary>
public sealed class ImportAllowance(TimeProvider clock)
{
    public const int ImportsPerHour = 10;
    public const int AttemptsPerHour = 30;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private sealed class Ledger
    {
        public readonly Queue<DateTimeOffset> Attempts = new();
        public readonly Queue<DateTimeOffset> Imports = new();
    }

    private readonly ConcurrentDictionary<Guid, Ledger> _people = new();

    /// <summary>
    /// Takes one attempt for this person, or says why not: the message, and
    /// how long until the next one would be let through.
    /// </summary>
    public (string Message, TimeSpan Wait)? TryBegin(Guid person)
    {
        var now = clock.GetUtcNow();
        var ledger = _people.GetOrAdd(person, _ => new Ledger());
        lock (ledger)
        {
            Trim(ledger.Attempts, now);
            Trim(ledger.Imports, now);
            if (ledger.Imports.Count >= ImportsPerHour)
            {
                var wait = ledger.Imports.Peek() + Window - now;
                return ($"You have imported {ImportsPerHour} packs in the last hour, which is the most allowed. "
                        + $"You can import another {In(wait)}.", wait);
            }
            if (ledger.Attempts.Count >= AttemptsPerHour)
            {
                var wait = ledger.Attempts.Peek() + Window - now;
                return ($"There have been {AttemptsPerHour} import attempts from your account in the last hour, which is the most allowed. "
                        + $"You can try again {In(wait)}.", wait);
            }
            ledger.Attempts.Enqueue(now);
            return null;
        }
    }

    /// <summary>The attempt went in: it counts towards the hourly imports.</summary>
    public void Imported(Guid person)
    {
        var ledger = _people.GetOrAdd(person, _ => new Ledger());
        lock (ledger) ledger.Imports.Enqueue(clock.GetUtcNow());
    }

    private static void Trim(Queue<DateTimeOffset> times, DateTimeOffset now)
    {
        while (times.Count > 0 && times.Peek() + Window <= now) times.Dequeue();
    }

    /// <summary>"in 1 minute", "in 23 minutes": a wait in the words a person would use.</summary>
    public static string In(TimeSpan wait)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
        return minutes == 1 ? "in a minute" : $"in {minutes} minutes";
    }
}
