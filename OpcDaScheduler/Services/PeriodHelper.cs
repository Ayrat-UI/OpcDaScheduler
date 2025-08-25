using System;
using System.Globalization;
using System.Linq;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public static class PeriodHelper
    {
        // Безопасно получаем TZ: если в AppConfig что-то не так — берём локальный.
        private static TimeZoneInfo? _tz;
        private static TimeZoneInfo Tz
        {
            get
            {
                if (_tz != null) return _tz;
                try { _tz = TimeZoneInfo.FindSystemTimeZoneById(AppConfig.TimeZoneId); }
                catch { _tz = TimeZoneInfo.Local; }
                return _tz!;
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
        /// Производственная дата: календарный день, к которому относятся текущие производственные сутки.
        /// Пример: start=22:00, время 25.08 14:00 -> prodDate = 26.08.
        /// </summary>
        public static DateTime GetProductionDate(DateTime local) =>
            GetDayStart(local).AddDays(1).Date;

        /// <summary>Начало текущей смены.</summary>
        public static DateTime GetShiftStart(DateTime local)
        {
            var (idx, start) = GetCurrentShiftIndex(local);
            return start;
        }

        /// <summary>Номер текущей смены.</summary>
        public static int GetShiftNo(DateTime local)
        {
            var (idx, _) = GetCurrentShiftIndex(local);
            if (idx < 0) return 1;
            return S.Shifts[idx].Number;
        }

        /// <summary>Календарное начало часа (без правил).</summary>
        public static DateTime GetHourStart(DateTime local) =>
            new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Kind);

        /// <summary>
        /// Час и производственная дата ДЛЯ ЗАПИСИ (часовые), с учётом правила:
        /// если текущее время >= ThresholdHour — писать как WriteAsHour,
        /// а дата при необходимости сдвигается на +1 день (ShiftDateToNextDay).
        /// Возвращает: номер часа, производственную дату (для legacy) и periodStart (для новой схемы).
        /// </summary>
        public static (int HourNo, DateTime ProductionDate, DateTime PeriodStart) GetHourForWrite(DateTime local)
        {
            var prodDate = GetProductionDate(local);     // базовая прод. дата
            var hourNo = local.Hour;

            var r = S.HourRule ?? new HourRule();
            if (r.Enabled && local.Hour >= r.ThresholdHour)
            {
                hourNo = Math.Clamp(r.WriteAsHour, 0, 23);
                if (r.ShiftDateToNextDay) prodDate = prodDate.AddDays(1);
            }

            // periodStart — реальное начало «того» часа в календаре
            var periodStart = new DateTime(local.Year, local.Month, local.Day, hourNo, 0, 0, local.Kind);
            return (hourNo, prodDate, periodStart);
        }

        /// <summary>
        /// АДАПТЕР ДЛЯ СТАРОГО КОДА: раньше использовался ApplyHourRule.
        /// Теперь всё делает GetHourForWrite, а этот метод просто прокидывает нужные поля.
        /// </summary>
        public static (int HourNo, DateTime DateForLegacy) ApplyHourRule(DateTime local)
        {
            var hw = GetHourForWrite(local);
            return (hw.HourNo, hw.ProductionDate);
        }

        /// <summary>
        /// Предыдущая (уже завершившаяся) смена на момент nowLocal и её производственная дата —
        /// используется при записи «на цикл назад».
        /// </summary>
        public static (int ShiftNo, DateTime ShiftStart, DateTime ProductionDate) GetPreviousShiftForWrite(DateTime nowLocal)
        {
            var shifts = S.Shifts ?? new System.Collections.Generic.List<ShiftDef>();
            var baseStart = GetDayStart(nowLocal);

            var rel = shifts
                .Select(sh =>
                {
                    var ts = ParseHHmm(sh.Start);
                    var relHour = Mod24(ts.TotalHours - S.ProductionDayStartHour); // 0..24
                    return new { sh, relHour };
                })
                .OrderBy(x => x.relHour)
                .ToList();

            if (rel.Count == 0)
                return (1, baseStart, GetProductionDate(baseStart));

            // Определяем текущую смену
            var hoursSince = (nowLocal - baseStart).TotalMinutes / 60.0; // 0..24)
            int curIdx = 0;
            for (int i = 0; i < rel.Count; i++)
            {
                double cur = rel[i].relHour;
                double next = rel[(i + 1) % rel.Count].relHour;

                bool inInterval = next > cur
                    ? (hoursSince >= cur && hoursSince < next)
                    : (hoursSince >= cur || hoursSince < next); // переход через 24

                if (inInterval) { curIdx = i; break; }
            }

            // Предыдущая смена
            int prevIdx = (curIdx - 1 + rel.Count) % rel.Count;
            double prevRel = rel[prevIdx].relHour;
            double curRel = rel[curIdx].relHour;

            var prevStart = baseStart.AddHours(prevRel);
            // Если prevRel > curRel — её старт «вчера» относительно baseStart
            if (prevRel > curRel) prevStart = prevStart.AddDays(-1);

            int prevNo = rel[prevIdx].sh.Number;
            var prodDate = GetProductionDate(prevStart);

            return (prevNo, prevStart, prodDate);
        }

        // ===== внутреннее =====

        private static (int idx, DateTime startLocal) GetCurrentShiftIndex(DateTime local)
        {
            if (S.Shifts == null || S.Shifts.Count == 0)
            {
                var ds = GetDayStart(local);
                return (-1, ds);
            }

            var baseStart = GetDayStart(local);                 // начало производственных суток
            var passed = local - baseStart;                     // сколько прошло
            var hPassed = passed.TotalMinutes / 60.0;           // [0..24)

            var rel = S.Shifts
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
                double next = rel[(i + 1) % rel.Count].relHour;

                bool inInterval = next > cur
                    ? (hPassed >= cur && hPassed < next)
                    : (hPassed >= cur || hPassed < next); // переход через 24

                if (inInterval)
                {
                    var start = baseStart.AddHours(cur);
                    return (i, start);
                }
            }

            var first = rel.First();
            return (0, baseStart.AddHours(first.relHour));
        }

        private static TimeSpan ParseHHmm(string s)
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
