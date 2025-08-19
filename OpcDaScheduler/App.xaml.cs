using OpcDaScheduler.Services;
using Serilog;
using System;
using System.Threading;
using System.Windows;

namespace OpcDaScheduler
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Логгер до всего
            LogService.Init();

            // Single instance
            bool createdNew;
            _mutex = new Mutex(initiallyOwned: true, name: AppConfig.MutexName, createdNew: out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Приложение уже запущено.", "OpcDaScheduler",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Log.Warning("Second instance attempt blocked by mutex {Name}", AppConfig.MutexName);
                Shutdown();
                return;
            }

            Log.Information("App starting...");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("App exiting...");
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            Serilog.Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
