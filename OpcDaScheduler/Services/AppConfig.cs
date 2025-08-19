using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace OpcDaScheduler.Services
{
    public static class AppConfig
    {
        private static IConfigurationRoot? _cfg;

        public static IConfigurationRoot Config => _cfg ??= Build();

        public static string ConnectionString =>
            Config.GetSection("Database")["ConnectionString"] ?? "";

        public static string TimeZoneId =>
            Config["TimeZone"] ?? "Asia/Tashkent";

        public static string LogPath =>
            Config.GetSection("Logging")["Path"] ?? "logs/app-.log";

        public static string LogLevel =>
            Config.GetSection("Logging")["Level"] ?? "Information";

        public static string MutexName =>
            Config["SingleInstanceMutex"] ?? "Global\\OpcDaScheduler_SingleInstance";

        private static IConfigurationRoot Build()
        {
            var baseDir = AppContext.BaseDirectory; // рядом с exe
            return new ConfigurationBuilder()
                .SetBasePath(baseDir)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
        }
    }
}
