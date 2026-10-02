using Npgsql;

namespace Tesria.Api.Infrastructure;

/// <summary>
/// Whether an exception is two requests colliding in the database rather
/// than something broken: a serializable transaction that lost (40001), a
/// deadlock (40P01), or a unique index that the other request filled first
/// (23505). Npgsql's execution strategy wraps these in an
/// InvalidOperationException ("likely due to a transient failure") and EF
/// Core in a DbUpdateException, so the whole chain is searched.
///
/// <para>These answered a bare 500 (T1-023, t2-026): the request that lost
/// the race got "Request failed (500)." although nothing was wrong except
/// the timing, and trying again would have given the ordinary answer.</para>
/// </summary>
public static class DbConflicts
{
    public static bool IsConflict(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState:
                    PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected
                    or PostgresErrorCodes.UniqueViolation })
                return true;
        return false;
    }
}
