using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using OpcDaScheduler.Models;
using OpcDaScheduler.Services;

namespace OpcDaScheduler
{
    public partial class SettingsDialog : Window
    {
        private PeriodSettings _s;
        private readonly ObservableCollection<ShiftDef> _shifts;

        public SettingsDialog()
        {
            InitializeComponent();

            for (int h = 0; h < 24; h++)
            {
                DayStartHour.Items.Add(h);
                HourThreshold.Items.Add(h);
                HourWriteAs.Items.Add(h);
            }

            _s = Clone(ConfigStore.Current.Period ?? PeriodSettings.CreateDefault());
            _shifts = new ObservableCollection<ShiftDef>(_s.Shifts.Select(Clone));
            ShiftsGrid.ItemsSource = _shifts;

            LoadToUI(_s);
        }

        private static PeriodSettings Clone(PeriodSettings s) => new PeriodSettings
        {
            ProductionDayStartHour = s.ProductionDayStartHour,
            HourRule = new HourRule
            {
                Enabled = s.HourRule.Enabled,
                ThresholdHour = s.HourRule.ThresholdHour,
                WriteAsHour = s.HourRule.WriteAsHour,
                ShiftDateToNextDay = s.HourRule.ShiftDateToNextDay
            },
            Shifts = s.Shifts.Select(Clone).ToList()
        };

        private static ShiftDef Clone(ShiftDef x) => new ShiftDef
        {
            Number = x.Number,
            Name = x.Name,
            Start = x.Start,
            LengthHours = x.LengthHours
        };

        private void LoadToUI(PeriodSettings s)
        {
            DayStartHour.SelectedItem = s.ProductionDayStartHour;

            _shifts.Clear();
            foreach (var sh in s.Shifts.Select(Clone))
                _shifts.Add(sh);

            HourRuleEnabled.IsChecked = s.HourRule.Enabled;
            HourThreshold.SelectedItem = s.HourRule.ThresholdHour;
            HourWriteAs.SelectedItem = s.HourRule.WriteAsHour;
            HourShiftDate.IsChecked = s.HourRule.ShiftDateToNextDay;

            TestDate.SelectedDate = DateTime.Today;
            if (string.IsNullOrWhiteSpace(TestTime.Text)) TestTime.Text = "12:00";

            RecalcDayPreview();
            RecalcHourPreview();
            RecalcAllPreview();
        }

        private bool TryBuildSettingsFromUI(out PeriodSettings result)
        {
            result = Clone(_s);

            if (DayStartHour.SelectedItem is int h)
                result.ProductionDayStartHour = h;

            result.Shifts = _shifts.Select(Clone).OrderBy(s => s.Number).ToList();

            result.HourRule.Enabled = (HourRuleEnabled.IsChecked == true);
            result.HourRule.ThresholdHour = HourThreshold.SelectedItem is int th ? th : result.HourRule.ThresholdHour;
            result.HourRule.WriteAsHour = HourWriteAs.SelectedItem is int wa ? wa : result.HourRule.WriteAsHour;
            result.HourRule.ShiftDateToNextDay = (HourShiftDate.IsChecked == true);

            foreach (var sh in result.Shifts)
            {
                if (!TimeSpan.TryParseExact(sh.Start, "hh\\:mm", CultureInfo.InvariantCulture, out _))
                {
                    MessageBox.Show($"Смена №{sh.Number}: неверное время «{sh.Start}» (ожидается HH:mm)",
                        "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (sh.LengthHours <= 0 || sh.LengthHours > 24)
                {
                    MessageBox.Show($"Смена №{sh.Number}: длительность 1..24 ч.",
                        "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private void AddShift_Click(object sender, RoutedEventArgs e)
        {
            var n = _shifts.Count > 0 ? _shifts.Max(s => s.Number) + 1 : 1;
            _shifts.Add(new ShiftDef { Number = n, Name = $"Смена {n}", Start = "00:00", LengthHours = 8 });
        }

        private void RemoveShift_Click(object sender, RoutedEventArgs e)
        {
            if (ShiftsGrid.SelectedItem is ShiftDef sh)
                _shifts.Remove(sh);
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Сбросить все настройки к заводским?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            _s = PeriodSettings.CreateDefault();
            LoadToUI(_s);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildSettingsFromUI(out var built)) return;

            _s = built;
            ConfigStore.Current.Period = _s;
            ConfigStore.Save();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ===== предпросмотры

        private void RecalcDayPreview()
        {
            var now = DateTime.Now;
            var saved = ConfigStore.Current.Period;

            if (TryBuildSettingsFromUI(out var tmp))
            {
                ConfigStore.Current.Period = tmp;
                var start = PeriodHelper.GetDayStart(now);
                PreviewDay.Text = $"Сегодня: {now}\nНачало производственных суток: {start}";
            }
            ConfigStore.Current.Period = saved;
        }

        private void RecalcHourPreview()
        {
            var now = DateTime.Now;
            var saved = ConfigStore.Current.Period;

            if (TryBuildSettingsFromUI(out var tmp))
            {
                ConfigStore.Current.Period = tmp;

                var hw = PeriodHelper.GetHourForWrite(now);
                var baseD = PeriodHelper.GetProductionDate(now);

                PreviewHour.Text =
                    $"Сейчас: {now:HH:mm}.\n" +
                    $"Час для записи ⇒ дата={hw.ProductionDate:yyyy-MM-dd}, час={hw.HourNo}, periodStart={hw.PeriodStart:yyyy-MM-dd HH\\:mm}\n" +
                    $"(базовая прод. дата без правил: {baseD:yyyy-MM-dd})";
            }
            ConfigStore.Current.Period = saved;
        }

        private void RecalcAllPreview()
        {
            var date = TestDate.SelectedDate ?? DateTime.Today;
            if (!TimeSpan.TryParseExact(TestTime.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var ts))
                ts = new TimeSpan(12, 0, 0);
            var dt = new DateTime(date.Year, date.Month, date.Day, ts.Hours, ts.Minutes, 0);

            var saved = ConfigStore.Current.Period;

            if (TryBuildSettingsFromUI(out var tmp))
            {
                ConfigStore.Current.Period = tmp;

                var dayStart = PeriodHelper.GetDayStart(dt);
                var currShiftNo = PeriodHelper.GetShiftNo(dt);
                var currShiftStart = PeriodHelper.GetShiftStart(dt);
                var prevShift = PeriodHelper.GetPreviousShiftForWrite(dt);
                var hw = PeriodHelper.GetHourForWrite(dt);

                PreviewAll.Text =
                    $"Тестовое время: {dt}\n" +
                    $"- Производственные сутки: старт {dayStart}\n" +
                    $"- Текущая смена: №{currShiftNo}, начало {currShiftStart}\n" +
                    $"- Смена для записи: №{prevShift.ShiftNo}, дата={prevShift.ProductionDate:yyyy-MM-dd}, начало {prevShift.ShiftStart}\n" +
                    $"- Час для записи: дата={hw.ProductionDate:yyyy-MM-dd}, час={hw.HourNo}, periodStart={hw.PeriodStart:yyyy-MM-dd HH\\:mm}";
            }
            ConfigStore.Current.Period = saved;
        }

        private void RecalcPreview_Click(object sender, RoutedEventArgs e)
        {
            RecalcDayPreview();
            RecalcHourPreview();
            RecalcAllPreview();
        }
    }
}
