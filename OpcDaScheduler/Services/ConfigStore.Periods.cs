using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public class UserConfig
    {
        public PeriodSettings Period { get; set; } = PeriodSettings.CreateDefault();
    }

    public static partial class ConfigStore
    {
        private static readonly JsonSerializerOptions _userJson = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string GetUserConfigPath()
            => Path.Combine(GetFolder(), "userconfig.json");

        public static UserConfig Current { get; private set; } = LoadUser();

        public static void Save()
        {
            var path = GetUserConfigPath();
            var json = JsonSerializer.Serialize(Current, _userJson);
            File.WriteAllText(path, json);
            Serilog.Log.Information("UserConfig saved: {Path}", path);
        }

        private static UserConfig LoadUser()
        {
            try
            {
                var path = GetUserConfigPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<UserConfig>(json, _userJson);
                    if (cfg != null) return cfg;
                }
            }
            catch { /* оставим дефолт */ }

            var def = new UserConfig();
            try { Save(); } catch { }
            return def;
        }
    }
}
