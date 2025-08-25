using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public static class PeriodHelper
    {
        // Часовой пояс из конфигурации (с безопасным фолбэком на локальный)
        private static TimeZoneInfo? _tz;
        private static TimeZoneInfo Tz
        {
            get
            {
                if (_tz != null) return _tz;
                try { _tz = TimeZoneInfo.FindSystemTimeZoneById(AppConfig.TimeZoneId); }
                catch { _tz = TimeZoneInfo.Local; }
                return _tz;
            }
        }

        private static PeriodSettings S => ConfigStore.Current.Period ?? PeriodSettings.CreateDefault();

        public static DateTime NowLocal() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);

        /// <summary>
        /// Начало производственных суток по настройке ProductionDayStartHour.
        /// Пример: start=22:00, 26.08 01:10 -> вернёт 25.08 22:00.
        /// </summary>
        public static DateTime GetDayStart(DateTime local)
        {
            int h = Math.Clamp(S.ProductionDayStartHour, 0, 23);
            var todayStart = new DateTime(local.Year, local.Month, local.Day, h, 0, 0, local.Kind);
            return (local >= todayStart) ? todayStart : todayStart.AddDays(-1);
        }

        /// <summary>
        /// Производственная дата (календарный день, к которому относятся текущие производственные сутки).
        /// Пример: start=22:00, время 25.08 14:00 -> prodDate = 26.08.
        /// </summary>
        public static DateTime GetProductionDate(DateTime local) =>
            GetDayStart(local).AddDays(1).Date;

        /// <summary>Номер текущей смены.</summary>
        public static int GetShiftNo(DateTime local)
        {
            var (idx, _) = GetCurrentShiftIndex(local);
            var shifts = S.Shifts ?? new List<ShiftDef>();
            if (idx < 0 || idx >= shifts.Count) return 1;
            return shifts[idx].Number;
        }

        /// <summary>Начало текущей смены.</summary>
        public static DateTime GetShiftStart(DateTime local)
        {
            var (_, start) = GetCurrentShiftIndex(local);
            return start;
        }

        /// <summary>Календарное начало часа.</summary>
        public static DateTime GetHourStart(DateTime local) =>
            new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Kind);

        /// <summary>
        /// Час и производственная дата ДЛЯ ЗАПИСИ (часовые).
        /// ВСЕГДА берём последний ПОЛНОСТЬЮ завершённый час (local → local.Hour - 1).
        /// Пример: 21:45 → 20:00; 00:05 → 23:00 предыдущего календарного дня.
        /// </summary>
        public static (int HourNo, DateTime ProductionDate, DateTime PeriodStart) GetHourForWrite(DateTime local)
        {
            // КЛЮЧЕВОЕ: прошлый час (никаких спец-правил!)
            var prevHourStart = GetHourStart(local).AddHours(-1);

            // Номер часа и прод.дата считаются именно для этого часа
            var hourNo = prevHourStart.Hour;
            var prodDate = GetProductionDate(prevHourStart);

            // periodStart — фактическое начало того часа
            return (hourNo, prodDate, prevHourStart);
        }

        /// <summary>
        /// Адаптер для старого кода (возвращает час и прод.дату).
        /// </summary>
        public static (int HourNo, DateTime DateForLegacy) ApplyHourRule(DateTime local)
        {
            var hw = GetHourForWrite(local);
            return (hw.HourNo, hw.ProductionDate);
        }

        /// <summary>
        /// Предыдущая (уже завершившаяся) смена на момент nowLocal и её прод.дата.
        /// </summary>
        public static (int ShiftNo, DateTime ShiftStart, DateTime ProductionDate) GetPreviousShiftForWrite(DateTime nowLocal)
        {
            var shifts = S.Shifts ?? new List<ShiftDef>();
            var baseStart = GetDayStart(nowLocal);

            if (shifts.Count == 0)
                return (1, baseStart, GetProductionDate(baseStart));

            var rel = shifts
                .Select(sh =>
                {
                    var ts = ParseHHmm(sh.Start);
                    var relHour = Mod24(ts.TotalHours - S.ProductionDayStartHour); // 0..24
                    return new { sh, relHour };
                })
                .OrderBy(x => x.relHour)
                .ToList();

            var hoursSince = (nowLocal - baseStart).TotalHours;
            int curIdx = 0;

            for (int i = 0; i < rel.Count; i++)
            {
                double cur = rel[i].relHour;
                double nxt = rel[(i + 1) % rel.Count].relHour;
                bool inInterval = nxt > cur
                    ? (hoursSince >= cur && hoursSince < nxt)
                    : (hoursSince >= cur || hoursSince < nxt); // переход через 24

                if (inInterval) { curIdx = i; break; }
            }

            int prevIdx = (curIdx - 1 + rel.Count) % rel.Count;
            var prevStart = baseStart.AddHours(rel[prevIdx].relHour);
            return (rel[prevIdx].sh.Number, prevStart, GetProductionDate(prevStart));
        }

        // ===== вспомогательное =====

        private static (int idx, DateTime startLocal) GetCurrentShiftIndex(DateTime local)
        {
            var shifts = S.Shifts ?? new List<ShiftDef>();
            if (shifts.Count == 0)
                return (0, GetDayStart(local));

            var baseStart = GetDayStart(local);
            var hPassed = (local - baseStart).TotalHours;

            var rel = shifts
                .Select(sh =>
                {
                    var ts = ParseHHmm(sh.Start);
                    var relHour = Mod24(ts.TotalHours - S.ProductionDayStartHour);
                    return new { sh, relHour };
                })
                .OrderBy(x => x.relHour)
                .ToList();

            for (int i = 0; i < rel.Count; i++)
            {
                double cur = rel[i].relHour;
                double nxt = rel[(i + 1) % rel.Count].relHour;

                bool inInterval = nxt > cur
                    ? (hPassed >= cur && hPassed < nxt)
                    : (hPassed >= cur || hPassed < nxt);

                if (inInterval)
                {
                    var start = baseStart.AddHours(cur);
                    return (i, start);
                }
            }

            var first = rel.First();
            return (0, baseStart.AddHours(first.relHour));
        }

        private static TimeSpan ParseHHmm(string? s)
        {
            if (TimeSpan.TryParseExact(s?.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var ts))
                return ts;
            if (int.TryParse(s, out var h) && h >= 0 && h <= 23)
                return TimeSpan.FromHours(h);
            return TimeSpan.Zero;
        }

        private static double Mod24(double x)
        {
            var r = x % 24.0;
            if (r < 0) r += 24.0;
            return r;
        }
    }
}
