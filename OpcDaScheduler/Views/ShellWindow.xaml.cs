using System.Windows;
using OpcDaScheduler.ViewModels;
using OpcDaScheduler.Views.Pages;

namespace OpcDaScheduler.Views
{
    public partial class ShellWindow : Window
    {
        private readonly MainViewModel _vm = new MainViewModel();

        public ShellWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            NavToOpc();
        }

        private void ResetToggles()
        {
            _vm.IsOpcSelected = false;
            _vm.IsHourlySelected = false;
        }

        private void NavToOpc()
        {
            ResetToggles();
            _vm.IsOpcSelected = true;
            _vm.Navigate(new OpcClientPage(),
                         "OPC-клиент",
                         "Подключение и выбор тегов");
        }

        private void NavToHourly()
        {
            ResetToggles();
            _vm.IsHourlySelected = true;
            _vm.Navigate(new HourlyPage(),
                         "Часовые",
                         "Автозапись каждый час в указанную минуту");
        }

        private void NavOpc_Click(object sender, RoutedEventArgs e) => NavToOpc();
        private void NavHourly_Click(object sender, RoutedEventArgs e) => NavToHourly();
    }
}
