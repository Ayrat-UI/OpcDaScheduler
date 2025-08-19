using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace OpcDaScheduler
{
    public partial class TagMappingDialog : Window
    {
        public ObservableCollection<TagMapEntry> Items { get; } = new();

        public TagMappingDialog()
        {
            InitializeComponent();
            DataContext = Items;
        }

        public void LoadFromOpcIds(string[] itemIds)
        {
            Items.Clear();
            foreach (var id in itemIds)
            {
                var alias = id;
                var p = id.Replace('\\', '/').Split('.', '/', '\\');
                if (p.Length > 0) alias = p[^1];

                Items.Add(new TagMapEntry
                {
                    OpcItemId = id,
                    Alias = alias,
                    TargetTagId = 0,
                    Formula = "x"
                });
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!Items.Any(i => i.TargetTagId > 0))
            {
                MessageBox.Show("Укажите хотя бы один TargetTagId.", "Проверка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
