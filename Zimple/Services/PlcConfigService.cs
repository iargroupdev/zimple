using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Zimple.Models;

namespace Zimple.Services
{
    public static class PlcConfigService
    {
        private static string AppFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Zimple");

        private static string FilePath =>
            Path.Combine(AppFolder, "plcconfig.json");

        public static List<PlcConfig> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<PlcConfig>();

                string json = File.ReadAllText(FilePath);

                var list = JsonConvert.DeserializeObject<List<PlcConfig>>(json);

                return list ?? new List<PlcConfig>();
            }
            catch
            {
                return new List<PlcConfig>();
            }
        }

        public static void Save(List<PlcConfig> list)
        {
            try
            {
                if (!Directory.Exists(AppFolder))
                    Directory.CreateDirectory(AppFolder);

                string json = JsonConvert.SerializeObject(list, Formatting.Indented);

                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // opcional: log interno
            }
        }
    }
}