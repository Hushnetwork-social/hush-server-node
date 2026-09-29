using Npgsql;

namespace HushNode.HushVoting.Licensing.Storage;

public static class LicenceDatabaseFailures
{
    /// <summary>Npgsql/EF may wrap serialization/deadlock faults in InvalidOperationException.</summary>
    public static bool IsSerializationConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }
}
