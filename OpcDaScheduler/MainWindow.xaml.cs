using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using OPCAutomation;

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

                _server = new OPCServer();                     // UI-нить WPF — STA, это ок
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
            foreach (object b in br)            // перечисляем ветви текущего уровня
            {
                string name = b.ToString()!;
                br.MoveDown(name);

                var branch = new Node { Name = name };

                br.ShowBranches();
                BuildTreeRecursive(br, branch);  // подветви

                br.ShowLeafs(true);              // листья в этой ветке
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
