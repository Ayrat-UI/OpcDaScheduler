using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public enum TagDataSchema
    {
        LegacyV1, // tag_name, date, period, hour_num, shift_num, value, source
        NewV2     // tagid, periodstart, periodtype, hourno, shiftno, value
    }

    public static class DbWriter
    {
        public static async Task<TagDataSchema> DetectTagDataSchemaAsync(NpgsqlConnection conn)
        {
            const string sql = @"
select lower(string_agg(column_name, ',')) 
from information_schema.columns 
where table_schema='public' and table_name='tag_data';";
            await using var cmd = new NpgsqlCommand(sql, conn);
            var cols = (string?)await cmd.ExecuteScalarAsync() ?? "";
            if (cols.Contains("tag_name") && cols.Contains("period") && cols.Contains("hour_num"))
                return TagDataSchema.LegacyV1;
            return TagDataSchema.NewV2;
        }

        // ---- NEW: гарантируем, что tag_name есть в справочнике, на который ссылается FK ----
        public static async Task<bool> EnsureLegacyTagExistsAsync(NpgsqlConnection conn, string tagName)
        {
            // Найдём таблицу/колонку, на которую ссылается tag_data.tag_name
            const string metaSql = @"
select ref.relname as ref_table,
       att2.attname as ref_column
from pg_constraint con
join pg_class      tbl   on tbl.oid   = con.conrelid
join pg_attribute  att   on att.attrelid = tbl.oid and att.attnum = any(con.conkey)
join pg_class      ref   on ref.oid   = con.confrelid
join pg_attribute  att2  on att2.attrelid = ref.oid and att2.attnum = any(con.confkey)
where tbl.relname = 'tag_data' and con.contype = 'f' and att.attname = 'tag_name'
limit 1;";
            await using var mc = new NpgsqlCommand(metaSql, conn);
            await using var r = await mc.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return true; // FK не найден — ничего делать не надо

            var refTable = r.GetString(0);
            var refCol = r.GetString(1);
            await r.DisposeAsync();

            // Попробуем вставить запись в справочник, если её нет
            var sql = $"insert into public.{refTable} ({refCol}) values (@v) on conflict do nothing;";
            await using var ins = new NpgsqlCommand(sql, conn);
            ins.Parameters.Add(new NpgsqlParameter("v", NpgsqlDbType.Text) { Value = tagName });

            try
            {
                await ins.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "EnsureLegacyTagExistsAsync: нет прав/не удалось вставить в {Table}.{Col}", refTable, refCol);
                return false;
            }
        }

        // ===== Legacy V1: UPDATE → INSERT, с явными типами =====
        public static async Task UpsertLegacyAsync(
            NpgsqlConnection conn,
            string tagName,
            DateTime date,
            string period,
            int? hourNum,
            int? shiftNum,
            double value,
            string source,
            CancellationToken ct = default)
        {
            const string updateSql = @"
update public.tag_data
   set value  = @value,
       source = @source
 where tag_name = @tag_name
   and period   = @period
   and date     = @date
   and ( ( @hour_num  is null and hour_num  is null ) or hour_num  = @hour_num )
   and ( ( @shift_num is null and shift_num is null ) or shift_num = @shift_num );";

            await using (var up = new NpgsqlCommand(updateSql, conn))
            {
                up.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Double) { Value = value });
                up.Parameters.Add(new NpgsqlParameter("source", NpgsqlDbType.Text) { Value = source });
                up.Parameters.Add(new NpgsqlParameter("tag_name", NpgsqlDbType.Text) { Value = tagName });
                up.Parameters.Add(new NpgsqlParameter("period", NpgsqlDbType.Text) { Value = period });
                up.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
                up.Parameters.Add(new NpgsqlParameter("hour_num", NpgsqlDbType.Integer) { Value = (object?)hourNum ?? DBNull.Value });
                up.Parameters.Add(new NpgsqlParameter("shift_num", NpgsqlDbType.Integer) { Value = (object?)shiftNum ?? DBNull.Value });

                var affected = await up.ExecuteNonQueryAsync(ct);
                if (affected > 0) return;
            }

            const string insertSql = @"
insert into public.tag_data (tag_name, date, period, hour_num, shift_num, value, source)
values (@tag_name, @date, @period, @hour_num, @shift_num, @value, @source);";

            await using var ins = new NpgsqlCommand(insertSql, conn);
            ins.Parameters.Add(new NpgsqlParameter("tag_name", NpgsqlDbType.Text) { Value = tagName });
            ins.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
            ins.Parameters.Add(new NpgsqlParameter("period", NpgsqlDbType.Text) { Value = period });
            ins.Parameters.Add(new NpgsqlParameter("hour_num", NpgsqlDbType.Integer) { Value = (object?)hourNum ?? DBNull.Value });
            ins.Parameters.Add(new NpgsqlParameter("shift_num", NpgsqlDbType.Integer) { Value = (object?)shiftNum ?? DBNull.Value });
            ins.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Double) { Value = value });
            ins.Parameters.Add(new NpgsqlParameter("source", NpgsqlDbType.Text) { Value = source });

            await ins.ExecuteNonQueryAsync(ct);
        }

        // ===== Новая схема V2 (без изменений) =====
        private const string UpsertV2Sql = @"
insert into tag_data (tagid, periodstart, periodtype, hourno, shiftno, value)
values (@tagid, @periodstart, @periodtype, @hourno, @shiftno, @value)
on conflict (tagid, periodtype, periodstart)
do update set value = excluded.value, hourno = excluded.hourno, shiftno = excluded.shiftno;";

        public static async Task UpsertAsync(NpgsqlConnection conn, TagDataRow d, CancellationToken ct = default)
        {
            await using var cmd = new NpgsqlCommand(UpsertV2Sql, conn);
            cmd.Parameters.AddWithValue("tagid", d.TagId);
            cmd.Parameters.AddWithValue("periodstart", d.PeriodStart);
            cmd.Parameters.AddWithValue("periodtype", d.PeriodType);
            cmd.Parameters.Add(new NpgsqlParameter("hourno", NpgsqlDbType.Integer) { Value = (object?)d.HourNo ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("shiftno", NpgsqlDbType.Integer) { Value = (object?)d.ShiftNo ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Double) { Value = d.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
