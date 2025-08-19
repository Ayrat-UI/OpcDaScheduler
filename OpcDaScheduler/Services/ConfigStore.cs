using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OpcDaScheduler.Models;

namespace OpcDaScheduler.Services
{
    public static class ConfigStore
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public sealed class Root
        {
            public List<SetConfig> Sets { get; set; } = new();
        }

        public static string GetFolder()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); // %ProgramData%
            var dir = Path.Combine(root, "OpcDaScheduler");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetPath() => Path.Combine(GetFolder(), "sets.json");

        public static async Task<Root> LoadAsync()
        {
            var path = GetPath();
            if (!File.Exists(path))
            {
                var empty = new Root();
                await SaveAsync(empty);
                return empty;
            }

            await using var fs = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<Root>(fs, _json);
            return data ?? new Root();
        }

        public static async Task SaveAsync(Root root)
        {
            var path = GetPath();
            await using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, root, _json);
        }
    }
}
