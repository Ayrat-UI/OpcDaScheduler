using System;
using System.Collections.Generic;

namespace OpcDaScheduler.Models
{
    public class ShiftDef
    {
        public int Number { get; set; }           // № смены (1..N)
        public string Name { get; set; } = "";    // подпись (необяз.)
        public string Start { get; set; } = "22:00"; // HH:mm локального времени (в пределах суток)
        public int LengthHours { get; set; } = 8; // длительность смены, ч
    }

    public class HourRule
    {
        public bool Enabled { get; set; } = true;
        public int ThresholdHour { get; set; } = 23; // если текущее время >= 23:00
        public int WriteAsHour { get; set; } = 22;   // писать как 22-й час
        public bool ShiftDateToNextDay { get; set; } = true; // и дата = дата + 1 день
    }

    /// <summary> Настройки производственных суток и смен. </summary>
    public class PeriodSettings
    {
        /// <summary> Час начала производственных суток (0..23). Пример: 22 — сутки начинаются в 22:00. </summary>
        public int ProductionDayStartHour { get; set; } = 22;

        /// <summary> Описание смен. Время <see cref="ShiftDef.Start"/> указывать локальным HH:mm. </summary>
        public List<ShiftDef> Shifts { get; set; } = new();

        /// <summary> Спец-правило для часовых записей. </summary>
        public HourRule HourRule { get; set; } = new();

        public static PeriodSettings CreateDefault() => new PeriodSettings
        {
            ProductionDayStartHour = 22,
            Shifts = new List<ShiftDef>
            {
                new ShiftDef{ Number = 1, Name="Смена 1", Start="06:00", LengthHours=8 },
                new ShiftDef{ Number = 2, Name="Смена 2", Start="14:00", LengthHours=8 },
                new ShiftDef{ Number = 3, Name="Смена 3", Start="22:00", LengthHours=8 },
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
