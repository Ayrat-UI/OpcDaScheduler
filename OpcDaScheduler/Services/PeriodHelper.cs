using System;

namespace OpcDaScheduler.Services
{
    public static class PeriodHelper
    {
        private static TimeZoneInfo? _tz;
        private static TimeZoneInfo Tz => _tz ??= TimeZoneInfo.FindSystemTimeZoneById(AppConfig.TimeZoneId);

        public static DateTime NowLocal() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);

        /// <summary> Начало производственных суток (22:00 предыдущего дня) для заданного локального времени. </summary>
        public static DateTime GetDayStart(DateTime local)
        {
            return local.Hour >= 22
                ? new DateTime(local.Year, local.Month, local.Day, 22, 0, 0)
                : new DateTime(local.AddDays(-1).Year, local.AddDays(-1).Month, local.AddDays(-1).Day, 22, 0, 0);
        }

        /// <summary> Номер смены для локального времени: 1=22:00–06:00, 2=06:00–14:00, 3=14:00–22:00. </summary>
        public static int GetShiftNo(DateTime local)
        {
            var t = local.TimeOfDay;
            if (t >= TimeSpan.FromHours(22) || t < TimeSpan.FromHours(6)) return 1;
            if (t < TimeSpan.FromHours(14)) return 2;
            return 3;
        }

        public static DateTime GetShiftStart(DateTime local)
        {
            var dayStart = GetDayStart(local);
            return GetShiftNo(local) switch
            {
                1 => dayStart,
                2 => dayStart.AddHours(8),
                3 => dayStart.AddHours(16),
                _ => dayStart
            };
        }

        public static DateTime GetHourStart(DateTime local) =>
            new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);
    }
}
