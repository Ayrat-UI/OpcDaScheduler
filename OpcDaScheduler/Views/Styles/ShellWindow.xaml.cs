using System.Windows;
using OpcDaScheduler.ViewModels;
using OpcDaScheduler.Views.Pages;

namespace OpcDaScheduler.Views
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
            // Открываем по умолчанию страницу OPC
            NavToOpc();
        }

        private MainViewModel VM => (MainViewModel)DataContext;

        private void ResetToggles()
        {
            VM.IsOpcSelected = VM.IsHourlySelected = false;
        }

        private void NavToOpc()
        {
            ResetToggles();
            VM.IsOpcSelected = true;
            VM.Navigate(new OpcClientPage(), "OPC-клиент", "Подключение и выбор тегов");
        }

        private void NavToHourly()
        {
            ResetToggles();
            VM.IsHourlySelected = true;
            VM.Navigate(new HourlyPage(), "Часовые", "Автозапись каждый час в указанную минуту");
        }

        private void NavOpc_Click(object sender, RoutedEventArgs e) => NavToOpc();
        private void NavHourly_Click(object sender, RoutedEventArgs e) => NavToHourly();
    }
}
