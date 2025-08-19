using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public static class DbWriter
    {
        private const string UpsertSql = @"
insert into tag_data (tagid, periodstart, periodtype, hourno, shiftno, value)
values (@tagid, @periodstart, @periodtype, @hourno, @shiftno, @value)
on conflict (tagid, periodtype, periodstart)
do update set value = excluded.value, hourno = excluded.hourno, shiftno = excluded.shiftno;";

        public static async Task UpsertAsync(NpgsqlConnection conn, TagDataRow d, CancellationToken ct = default)
        {
            await using var cmd = new NpgsqlCommand(UpsertSql, conn);
            cmd.Parameters.AddWithValue("tagid", d.TagId);
            cmd.Parameters.AddWithValue("periodstart", d.PeriodStart);
            cmd.Parameters.AddWithValue("periodtype", d.PeriodType);
            cmd.Parameters.AddWithValue("hourno", (object?)d.HourNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("shiftno", (object?)d.ShiftNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("value", d.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
