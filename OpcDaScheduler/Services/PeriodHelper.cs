using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public static class PeriodHelper
    {
        private static TimeZoneInfo? _tz;
        private static TimeZoneInfo Tz => _tz ??= TimeZoneInfo.FindSystemTimeZoneById(AppConfig.TimeZoneId);
        private static PeriodSettings S => ConfigStore.Current.Period ?? PeriodSettings.CreateDefault();

        public static DateTime NowLocal() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);

        /// <summary>Начало производственных суток по настройке ProductionDayStartHour.</summary>
        public static DateTime GetDayStart(DateTime local)
        {
            int h = Math.Clamp(S.ProductionDayStartHour, 0, 23);
            var todayStart = new DateTime(local.Year, local.Month, local.Day, h, 0, 0, local.Kind);
            return (local >= todayStart) ? todayStart : todayStart.AddDays(-1);
        }

        /// <summary>
        /// Производственная дата: дата календарного дня, к которому относятся текущие произв. сутки.
        /// Пример: при старте суток 22:00 время 25.08 14:00 → prodDate = 25.08
        /// </summary>
        public static DateTime GetProductionDate(DateTime local) =>
            GetDayStart(local).AddDays(1).Date;

        /// <summary>Начало текущей смены по списку Shifts.</summary>
        public static DateTime GetShiftStart(DateTime local)
        {
            var (idx, start) = GetCurrentShiftIndex(local);
            return start;
        }

        /// <summary>Номер текущей смены по списку Shifts.</summary>
        public static int GetShiftNo(DateTime local)
        {
            var (idx, _) = GetCurrentShiftIndex(local);
            if (idx < 0) return 1;
            return S.Shifts[idx].Number;
        }

        /// <summary>Календарное начало часа (без спец-правил).</summary>
        public static DateTime GetHourStart(DateTime local) =>
            new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Kind);

        /// <summary>
        /// Спец-правило для часовой записи:
        /// если текущее время >= Threshold — писать как WriteAsHour,
        /// и, при необходимости, сдвигать производственную дату на +1 день.
        /// Возвращает (часы, датаДляLegacyDATE).
        /// </summary>
        public static (int HourNo, DateTime DateForLegacy) ApplyHourRule(DateTime local)
        {
            var prodDate = GetDayStart(local).Date;
            var hourNo = local.Hour;

            var r = S.HourRule ?? new HourRule();
            if (r.Enabled && local.Hour >= r.ThresholdHour)
            {
                hourNo = Math.Clamp(r.WriteAsHour, 0, 23);
                if (r.ShiftDateToNextDay) prodDate = prodDate.AddDays(1);
            }
            return (hourNo, prodDate);
        }

        /// <summary>
        /// Возвращает целевой час для записи в БД по текущим настройкам:
        /// (HourNo, ProductionDate, PeriodStart).
        /// ProductionDate — DATE для legacy-схемы; PeriodStart — точка начала часа для новой схемы.
        /// </summary>
        public static (int HourNo, DateTime ProductionDate, DateTime PeriodStart) GetHourForWrite(DateTime local)
        {
            var (hourNo, prodDate) = ApplyHourRule(local);
            // PeriodStart считаем как локальное время prodDate + hourNo:00
            var periodStart = new DateTime(prodDate.Year, prodDate.Month, prodDate.Day, hourNo, 0, 0, local.Kind);
            return (hourNo, prodDate, periodStart);
        }

        /// <summary>
        /// Вернуть предыдущую (уже завершившуюся) смену на момент nowLocal
        /// и её производственную дату — используется при записи «на цикл назад».
        /// </summary>
        public static (int ShiftNo, DateTime ShiftStart, DateTime ProductionDate) GetPreviousShiftForWrite(DateTime nowLocal)
        {
            var shifts = S.Shifts ?? new List<ShiftDef>();
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

            // Определим текущую смену
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
            double curRel = rel[curIdx].relHour;
            double prevRel = rel[prevIdx].relHour;

            var prevStart = baseStart.AddHours(prevRel);
            // Если prevRel > curRel, значит её старт — «вчера» относительно baseStart
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
