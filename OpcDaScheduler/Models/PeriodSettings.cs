using System;
using System.Collections.Generic;

namespace OpcDaScheduler.Models
{
    /// <summary>Описание одной смены.</summary>
    public class ShiftDef
    {
        /// <summary>Номер смены (1..N).</summary>
        public int Number { get; set; }

        /// <summary>Произвольное имя/подпись смены.</summary>
        public string Name { get; set; } = "";

        /// <summary>Время начала (локальное HH:mm) в пределах производственных суток.</summary>
        public string Start { get; set; } = "22:00";

        /// <summary>Длительность смены в часах.</summary>
        public int LengthHours { get; set; } = 8;
    }

    /// <summary>Правило для часовой записи (особый случай после порога).</summary>
    public class HourRule
    {
        /// <summary>Включить правило.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Если текущее время >= этого часа — применяем запись как указанный час.</summary>
        public int ThresholdHour { get; set; } = 23;

        /// <summary>Записывать как этот час (0..23), если сработал порог.</summary>
        public int WriteAsHour { get; set; } = 22;

        /// <summary>Сдвигать производственную дату на +1 день при срабатывании правила.</summary>
        public bool ShiftDateToNextDay { get; set; } = true;
    }

    /// <summary>Настройки производственных суток и смен.</summary>
    public class PeriodSettings
    {
        /// <summary>Час начала производственных суток (0..23). Пример: 22 — сутки начинаются в 22:00.</summary>
        public int ProductionDayStartHour { get; set; } = 22;

        /// <summary>Список смен. Время <see cref="ShiftDef.Start"/> указывать в локальном HH:mm.</summary>
        public List<ShiftDef> Shifts { get; set; } = new();

        /// <summary>Спец-правило для часовых записей.</summary>
        public HourRule HourRule { get; set; } = new();

        /// <summary>Заводские (дефолтные) настройки.</summary>
        public static PeriodSettings CreateDefault() => new PeriodSettings
        {
            ProductionDayStartHour = 22,
            Shifts = new List<ShiftDef>
            {
                new ShiftDef { Number = 1, Name = "Смена 1", Start = "22:00", LengthHours = 8 },
                new ShiftDef { Number = 2, Name = "Смена 2", Start = "06:00", LengthHours = 8 },
                new ShiftDef { Number = 3, Name = "Смена 3", Start = "14:00", LengthHours = 8 },
            },
            HourRule = new HourRule
            {
                Enabled = true,
                ThresholdHour = 23,
                WriteAsHour = 22,
                ShiftDateToNextDay = true
            }
        };
    }
}
