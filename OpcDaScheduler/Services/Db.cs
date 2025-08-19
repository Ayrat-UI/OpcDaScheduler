using Npgsql;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpcDaScheduler.Services
{
    public static class Db
    {
        public static async Task<bool> PingAsync(string connString, CancellationToken ct = default)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connString);
                await conn.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand("select 1", conn);
                var r = await cmd.ExecuteScalarAsync(ct);
                Log.Information("PostgreSQL ping ok: {Result}", r);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PostgreSQL ping failed");
                return false;
            }
        }
    }
}
