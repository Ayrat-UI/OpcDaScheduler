using System;
using System.Collections.Generic;

namespace OpcDaScheduler.Models
{
    public enum PeriodType { Hour, Shift, Day }

    public sealed class ScheduleConfig
    {
        // Mode: Hour/Shift/Day/EveryN/CustomCron (UI добавим позже)
        public string Mode { get; set; } = "Hour";
        public string? Cron { get; set; }
        public int? EveryMinutes { get; set; }
    }

    public sealed class SetItem
    {
        public string OpcItemId { get; set; } = "";
        public string Alias { get; set; } = "";
        public int TargetTagId { get; set; }            // ID в вашей БД
        public string Unit { get; set; } = "";
        public string Formula { get; set; } = "x";      // пока заглушка
        public PeriodType Aggregation { get; set; } = PeriodType.Hour;
        public bool Enabled { get; set; } = true;

        // удобства
        public override string ToString() => $"{Alias} => {TargetTagId} ({Aggregation})";
    }

    public sealed class SetConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("n");
        public string Name { get; set; } = "New Set";
        public string OpcServerProgId { get; set; } = "";
        public ScheduleConfig Schedule { get; set; } = new();
        public List<SetItem> Items { get; set; } = new();
    }

    // Модель записи в таблицу tag_data (для upsert)
    public sealed class TagDataRow
    {
        public int TagId { get; set; }
        public DateTime PeriodStart { get; set; }   // используем локальное время; можно перейти на UTC, если решим
        public string PeriodType { get; set; } = "Hour"; // "Hour"/"Shift"/"Day"
        public int? HourNo { get; set; }
        public int? ShiftNo { get; set; }
        public double Value { get; set; }
    }
}
