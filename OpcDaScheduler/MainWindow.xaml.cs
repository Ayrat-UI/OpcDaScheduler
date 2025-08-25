using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using OPCAutomation;

using Serilog;
using Npgsql;

// наши сервисы/модели
using OpcDaScheduler.Services;
using OpcDaScheduler.Models;

namespace OpcDaScheduler
{
    public partial class MainWindow : Window
    {
        private OPCServer? _server;
        private Node? _root;
        private readonly ObservableCollection<ReadRow> _results = new();

        public MainWindow()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = _results;

            // Если в XAML нет Loaded="Window_Loaded", можно раскомментировать:
            // this.Loaded += Window_Loaded;
        }

        // ======== Диагностика подключения к БД при старте =========
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var ok = await Db.PingAsync(AppConfig.ConnectionString);
            MessageBox.Show(
                ok ? "PostgreSQL: OK" : "PostgreSQL: FAILED.\nПроверьте ConnectionString в appsettings.json",
                "Diagnostics",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);

            Log.Information("Diagnostics shown. DB={Status}", ok ? "OK" : "FAILED");

            // Отладка расчёта периодов (час/смена/сутки)
            var now = PeriodHelper.NowLocal();
            var hourStart = PeriodHelper.GetHourStart(now);
            var shiftNo = PeriodHelper.GetShiftNo(now);
            var shiftStart = PeriodHelper.GetShiftStart(now);
            var dayStart = PeriodHelper.GetDayStart(now);
            Log.Information("Period debug: now={Now}, hourStart={HourStart}, shiftStart={ShiftStart}, dayStart={DayStart}, shift={Shift}",
                now, hourStart, shiftStart, dayStart, shiftNo);
        }
        // ==========================================================

        // Открыть окно настроек периодов
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                Log.Information("Settings updated: dayStart={H}, shifts={N}, hourRule={EN}",
                    ConfigStore.Current.Period.ProductionDayStartHour,
                    ConfigStore.Current.Period.Shifts?.Count ?? 0,
                    ConfigStore.Current.Period.HourRule?.Enabled ?? false);
                MessageBox.Show("Настройки сохранены.", "Настройки",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Найти OPC-DA серверы на указанном хосте (OPCEnum)
        private void RefreshServers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = new OPCServer();
                var arr = (Array)s.GetOPCServers(HostBox.Text.Trim());

                var list = arr.Cast<object?>()
                              .Select(o => o?.ToString())
                              .Where(x => !string.IsNullOrWhiteSpace(x))
                              .ToList();

                ServersBox.ItemsSource = list;
                if (ServersBox.Items.Count > 0) ServersBox.SelectedIndex = 0;

                MessageBox.Show($"Найдено серверов: {list.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка поиска серверов");
            }
        }

        // Подключение к выбранному серверу и построение дерева тегов
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (ServersBox.SelectedItem is not string progId)
            {
                MessageBox.Show("Выберите сервер из списка.");
                return;
            }

            try
            {
                DisconnectInternal();

                _server = new OPCServer(); // UI-нить WPF — STA, это ок
                _server.Connect(progId, HostBox.Text.Trim());

                var br = _server.CreateBrowser();
                br.ShowBranches();

                _root = new Node { Name = progId };

                // Рекурсивно обходим ветви
                BuildTreeRecursive(br, _root);

                // Листья на верхнем уровне (если есть)
                br.ShowLeafs(true);
                foreach (object leaf in br)
                {
                    string leafName = leaf.ToString()!;
                    string itemId = br.GetItemID(leafName);
                    _root.Children.Add(new Node { Name = leafName, ItemId = itemId });
                }

                BrowseTree.ItemsSource = new[] { _root };
                MessageBox.Show("Подключено и выполнен browse.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка подключения");
                DisconnectInternal();
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e) => DisconnectInternal();

        private void DisconnectInternal()
        {
            try { _server?.Disconnect(); } catch { }
            _server = null;
            _root = null;
            BrowseTree.ItemsSource = null;
            _results.Clear();
        }

        // Рекурсивный обход дерева OPCBrowser: ветви + листья
        private void BuildTreeRecursive(OPCBrowser br, Node parent)
        {
            foreach (object b in br) // перечисляем ветви текущего уровня
            {
                string name = b.ToString()!;
                br.MoveDown(name);

                var branch = new Node { Name = name };

                br.ShowBranches();
                BuildTreeRecursive(br, branch); // подветви

                br.ShowLeafs(true); // листья в этой ветке
                foreach (object leaf in br)
                {
                    string leafName = leaf.ToString()!;
                    string itemId = br.GetItemID(leafName);
                    branch.Children.Add(new Node { Name = leafName, ItemId = itemId });
                }

                parent.Children.Add(branch);

                br.MoveUp();
                br.ShowBranches();
            }
        }

        // Прочитать отмеченные листья и показать в таблице
        private void ReadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_server == null || _root == null)
            {
                MessageBox.Show("Нет подключения к OPC-серверу.");
                return;
            }

            var selected = GetCheckedLeaves(_root).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Отметьте галочками хотя бы один тег.");
                return;
            }

            // Читаем в отдельной STA-нити (COM)
            var t = new Thread(() =>
            {
                try
                {
                    var g = _server.OPCGroups.Add("read_group_" + Guid.NewGuid().ToString("N"));
                    g.IsActive = true; g.IsSubscribed = false;
                    var items = g.OPCItems;

                    Dispatcher.Invoke(() => _results.Clear());

                    int handle = 1;
                    foreach (var tag in selected)
                    {
                        try
                        {
                            var it = items.AddItem(tag, handle++);
                            it.Read((short)OPCDataSource.OPCDevice, out object v, out object q, out object ts);

                            var row = new ReadRow
                            {
                                Tag = tag,
                                Value = v?.ToString(),
                                Quality = Convert.ToInt16(q),
                                Timestamp = ts is DateTime dt ? dt : DateTime.MinValue
                            };

                            Dispatcher.Invoke(() => _results.Add(row));
                        }
                        catch (Exception exItem)
                        {
                            Dispatcher.Invoke(() => _results.Add(new ReadRow
                            {
                                Tag = tag,
                                Value = "ERR: " + exItem.Message,
                                Quality = 0,
                                Timestamp = DateTime.MinValue
                            }));
                        }
                    }

                    try { _server.OPCGroups.Remove(g.Name); } catch { }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show(ex.Message, "Ошибка чтения"));
                }
            });

            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // ==================== НОВОЕ: запись в БД ====================

        // Кнопки из XAML
        private void WriteHour_Click(object sender, RoutedEventArgs e) => StartWrite(PeriodType.Hour);
        private void WriteShift_Click(object sender, RoutedEventArgs e) => StartWrite(PeriodType.Shift);
        private void WriteDay_Click(object sender, RoutedEventArgs e) => StartWrite(PeriodType.Day);

        // Запуск мастера записи
        private void StartWrite(PeriodType type)
        {
            if (_server == null || _root == null)
            {
                MessageBox.Show("Нет подключения к OPC-серверу.");
                return;
            }

            var selected = GetCheckedLeaves(_root).ToArray();
            if (selected.Length == 0)
            {
                MessageBox.Show("Отметьте галочками хотя бы один тег.");
                return;
            }

            // Диалог сопоставления OPC→TagId/Formula
            var dlg = new TagMappingDialog { Owner = this };
            dlg.LoadFromOpcIds(selected);
            if (dlg.ShowDialog() != true) return;

            var map = dlg.Items.Where(i => i.TargetTagId > 0 || !string.IsNullOrWhiteSpace(i.Alias))
                               .ToDictionary(x => x.OpcItemId, x => x);

            if (map.Count == 0)
            {
                MessageBox.Show("Не указаны данные для записи (TagId/Alias).", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Вся логика в STA-потоке (COM)
            var t = new Thread(() => WriteSelectedAsync(type, map));
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // Основная логика записи
        private async void WriteSelectedAsync(PeriodType type, Dictionary<string, TagMapEntry> map)
        {
            try
            {
                if (_server == null)
                {
                    Dispatcher.Invoke(() => MessageBox.Show("Нет подключения к OPC-серверу."));
                    return;
                }

                // 1) Разовое чтение текущих значений
                var values = ReadValuesOnce(_server, map.Keys);

                // 2) Расчёт временных границ с учётом настроек
                var now = PeriodHelper.NowLocal();
                DateTime periodStart;
                string periodType;
                int? hourNo = null;
                int? shiftNo = null;

                // Для legacy-схемы заранее посчитаем prodDate
                DateTime? legacyProdDate = null;

                switch (type)
                {
                    case PeriodType.Hour:
                        {
                            // Применяем спец-правило: и час, и дата
                            var hr = PeriodHelper.ApplyHourRule(now);
                            hourNo = hr.HourNo;
                            legacyProdDate = hr.DateForLegacy;

                            // !!! periodStart для НОВОЙ схемы должен соответствовать правилу:
                            // дата = DateForLegacy, час = HourNo
                            periodStart = new DateTime(
                                hr.DateForLegacy.Year, hr.DateForLegacy.Month, hr.DateForLegacy.Day,
                                hr.HourNo, 0, 0, now.Kind);

                            periodType = "Hour";
                            break;
                        }

                    case PeriodType.Shift:
                        {
                            // Пишем за ПРЕДЫДУЩУЮ (завершившуюся) смену
                            var prev = PeriodHelper.GetPreviousShiftForWrite(now);
                            periodStart = prev.ShiftStart;     // для новой схемы (periodstart)
                            periodType = "Shift";
                            shiftNo = prev.ShiftNo;
                            legacyProdDate = prev.ProductionDate; // для legacy (DATE)
                            break;
                        }

                    default: // Day
                        {
                            periodStart = PeriodHelper.GetDayStart(now);
                            periodType = "Day";
                            legacyProdDate = PeriodHelper.GetProductionDate(now);
                            break;
                        }
                }

                // 3) Запись в БД (апсерт)
                int ok = 0, skip = 0, fail = 0;
                await using var conn = new NpgsqlConnection(AppConfig.ConnectionString);
                await conn.OpenAsync();

                // определить схему tag_data (legacy или новая)
                var schema = await DbWriter.DetectTagDataSchemaAsync(conn);

                foreach (var kv in map)
                {
                    var itemId = kv.Key;
                    var cfg = kv.Value;

                    if (!values.TryGetValue(itemId, out var raw) || raw is null)
                    {
                        Log.Warning("Skip write: no value for {ItemId}", itemId);
                        skip++; continue;
                    }

                    double x;
                    try
                    {
                        x = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        Log.Warning("Skip write: value not convertible to double. {ItemId}={Raw}", itemId, raw);
                        skip++; continue;
                    }

                    var y = FormulaEngine.Eval(cfg.Formula, x, now, shiftNo ?? 0);

                    try
                    {
                        if (schema == TagDataSchema.LegacyV1)
                        {
                            var prodDate = legacyProdDate ?? PeriodHelper.GetProductionDate(now);
                            var periodStr = periodType.ToLowerInvariant(); // "hour"/"shift"/"day"
                            var tagName = string.IsNullOrWhiteSpace(cfg.Alias) ? itemId : cfg.Alias;

                            // Гарантируем, что tag_name существует в справочнике (FK)
                            await DbWriter.EnsureLegacyTagExistsAsync(conn, tagName);

                            await DbWriter.UpsertLegacyAsync(
                                conn,
                                tagName: tagName,
                                date: prodDate,
                                period: periodStr,
                                hourNum: hourNo,
                                shiftNum: shiftNo,
                                value: y,
                                source: "OpcDaScheduler");
                        }
                        else
                        {
                            // Новая схема (tagid/periodstart/...)
                            var row = new TagDataRow
                            {
                                TagId = cfg.TargetTagId,
                                PeriodStart = periodStart,
                                PeriodType = periodType,
                                HourNo = hourNo,
                                ShiftNo = shiftNo,
                                Value = y
                            };
                            await DbWriter.UpsertAsync(conn, row);
                        }

                        ok++;
                    }
                    catch (Exception exUp)
                    {
                        Log.Error(exUp, "Upsert failed for map ItemId={ItemId}", itemId);
                        fail++;
                    }
                }

                Dispatcher.Invoke(() => MessageBox.Show(
                    $"Готово: записано {ok}, пропущено {skip}, ошибок {fail}.",
                    "Запись в БД",
                    MessageBoxButton.OK,
                    fail == 0 ? MessageBoxImage.Information : MessageBoxImage.Exclamation));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show(ex.Message, "Ошибка записи",
                    MessageBoxButton.OK, MessageBoxImage.Error));
                Log.Error(ex, "WriteSelected failed");
            }
        }

        // Одноразовое чтение значений по списку ItemId
        private static Dictionary<string, object?> ReadValuesOnce(OPCServer server, IEnumerable<string> itemIds)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            OPCGroup? g = null;
            try
            {
                g = server.OPCGroups.Add("write_read_" + Guid.NewGuid().ToString("N"));
                g.IsActive = true; g.IsSubscribed = false;
                var items = g.OPCItems;

                int handle = 1;
                foreach (var id in itemIds)
                {
                    try
                    {
                        var it = items.AddItem(id, handle++);
                        it.Read((short)OPCDataSource.OPCDevice, out object v, out object q, out object ts);
                        result[id] = v;
                    }
                    catch (Exception exAdd)
                    {
                        Log.Warning(exAdd, "Cannot add/read OPC item {ItemId}", id);
                        result[id] = null;
                    }
                }
            }
            finally
            {
                try { if (g != null) server.OPCGroups.Remove(g.Name); } catch { }
            }
            return result;
        }

        // Сбор всех отмеченных ItemID
        private static IEnumerable<string> GetCheckedLeaves(Node node)
        {
            foreach (var ch in node.Children)
            {
                if (ch.IsLeaf && ch.IsChecked)
                    yield return ch.ItemId!;

                foreach (var id in GetCheckedLeaves(ch))
                    yield return id;
            }
        }
    }
}
