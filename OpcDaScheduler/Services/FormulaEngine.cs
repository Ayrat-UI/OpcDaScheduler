using System;
using Serilog;

namespace OpcDaScheduler.Services
{
    public static class FormulaEngine
    {
        /// <summary>
        /// Пока просто возвращает x. Если формула не "x" — пишем предупреждение в лог.
        /// Позже подключим реальный движок.
        /// </summary>
        public static double Eval(string formula, double x, DateTime local, int shift)
        {
            if (!string.IsNullOrWhiteSpace(formula) && formula.Trim() != "x")
                Log.Warning("Formula engine stub: '{Formula}' not applied, returned x as-is", formula);

            return x;
        }
    }
}
