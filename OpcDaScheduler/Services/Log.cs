using System.IO;
using Serilog;
using Serilog.Events;

namespace OpcDaScheduler.Services
{
    public static class LogService
    {
        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;

            var logPath = AppConfig.LogPath;
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var level = ParseLevel(AppConfig.LogLevel);

            var cfg = new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    shared: true,
                    flushToDiskInterval: System.TimeSpan.FromSeconds(1),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

#if DEBUG
            // Видно в Visual Studio: View → Output → Show output from: Debug
            cfg = cfg.WriteTo.Debug(outputTemplate:
                "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
#endif

            // Консоль (для WPF не обязательна). Требуется пакет Serilog.Sinks.Console.
            cfg = cfg.WriteTo.Console(outputTemplate:
                "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            Log.Logger = cfg.CreateLogger();
            _initialized = true;

            Log.Information("Logger initialized. Path={Path}, Level={Level}", logPath, level);
        }

        private static LogEventLevel ParseLevel(string? level) =>
            level?.ToLowerInvariant() switch
            {
                "verbose" => LogEventLevel.Verbose,
                "debug" => LogEventLevel.Debug,
                "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                "fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
    }
}
