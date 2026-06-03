using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Windows;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using Serilog;

namespace KioskClinicaPC.Services
{
    public class HardwareDiscoveryService : IHardwareService
    {
        public async Task<AppConfig> GetHardwareInfoAsync()
        {
            var config = new AppConfig();
            await Task.Run(() =>
            {
                config.Cpu = GetCpuName();
                config.Cores = GetCpuCores();
                config.Ram = GetRamDetails();
                config.Gpu = GetGpuName();
                config.Storage = GetStorageDetails();
                config.Screen = GetScreenResolution();
                config.Os = $"{GetOsName()} ({GetPcName()})";

                // Identidad real del equipo (sustituye el hardcode "ASUS ROG").
                config.ChassisName = GetManufacturer();
                config.ModelName = GetModel();
                config.Sku = GetSku();
                config.Family = GetChassisFamily();

                // Componentes opcionales: null si el equipo no los tiene (no se muestran).
                config.Battery = GetBatteryInfo();
                config.Wifi = GetWifiAdapter();
                config.Camera = GetCameraName();
                // Puertos no se detectan de forma fiable por WMI: se deja como override manual (Settings).
            });
            return config;
        }

        /// <summary>Igual que GetWmiProperty pero devuelve null cuando el valor no existe (componente ausente),
        /// en vez de la cadena "No disponible". Permite ocultar componentes no presentes.</summary>
        private string? GetWmiPropertyOrNull(string wmiClass, string property, string? where = null)
        {
            try
            {
                string query = where == null
                    ? $"select {property} from {wmiClass}"
                    : $"select {property} from {wmiClass} where {where}";
                using var searcher = new ManagementObjectSearcher(query);
                var first = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                string? value = first?[property]?.ToString()?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener propiedad WMI opcional {Property} de {WmiClass}", property, wmiClass);
                return null;
            }
        }

        private string? GetManufacturer()
        {
            var m = GetWmiPropertyOrNull("Win32_ComputerSystem", "Manufacturer")
                    ?? GetWmiPropertyOrNull("Win32_ComputerSystemProduct", "Vendor");
            if (m == null) return null;
            // Limpia ruido habitual de OEM ("ASUSTeK COMPUTER INC." → "ASUSTeK COMPUTER").
            m = m.Replace(" INC.", "", StringComparison.OrdinalIgnoreCase)
                 .Replace(" CO., LTD.", "", StringComparison.OrdinalIgnoreCase)
                 .Replace(", LTD.", "", StringComparison.OrdinalIgnoreCase)
                 .Trim();
            return m.Length == 0 ? null : m;
        }

        private string? GetModel()
        {
            var model = GetWmiPropertyOrNull("Win32_ComputerSystem", "Model")
                        ?? GetWmiPropertyOrNull("Win32_ComputerSystemProduct", "Name");
            if (model == null) return null;
            // Filtra modelos genéricos sin valor ("System Product Name", "To be filled by O.E.M.").
            string lower = model.ToLowerInvariant();
            if (lower.Contains("to be filled") || lower.Contains("system product name") || lower.Contains("default string"))
                return null;
            return model;
        }

        private string? GetSku()
        {
            var sku = GetWmiPropertyOrNull("Win32_ComputerSystem", "SystemSKUNumber")
                      ?? GetWmiPropertyOrNull("Win32_ComputerSystemProduct", "IdentifyingNumber");
            if (sku == null) return null;
            string lower = sku.ToLowerInvariant();
            if (lower.Contains("to be filled") || lower.Contains("default string") || lower == "sku")
                return null;
            return sku;
        }

        /// <summary>Tipo de chasis legible (Portátil / Sobremesa / All-in-One) desde Win32_SystemEnclosure.</summary>
        private string? GetChassisFamily()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select ChassisTypes from Win32_SystemEnclosure");
                var first = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                if (first?["ChassisTypes"] is ushort[] types && types.Length > 0)
                {
                    int t = types[0];
                    // Códigos SMBIOS de Win32_SystemEnclosure.ChassisTypes.
                    if (t is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 31) return "Portátil";
                    if (t is 13) return "All-in-One";
                    if (t is 3 or 4 or 5 or 6 or 7 or 15 or 16 or 35) return "Sobremesa";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener el tipo de chasis.");
            }
            return null;
        }

        /// <summary>Capacidad/presencia de batería. null en sobremesa (sin batería).</summary>
        private string? GetBatteryInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select EstimatedChargeRemaining, Chemistry from Win32_Battery");
                var battery = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                if (battery == null) return null; // sobremesa: sin batería

                // Capacidad de diseño en Wh desde root\wmi (más fiable que Win32_Battery).
                double wh = GetBatteryDesignWh();
                return wh > 0 ? $"{Math.Round(wh)} Wh" : "Batería integrada";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al detectar la batería.");
                return null;
            }
        }

        private double GetBatteryDesignWh()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "select DesignedCapacity from BatteryStaticData");
                var data = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                if (data?["DesignedCapacity"] != null)
                {
                    double mWh = Convert.ToDouble(data["DesignedCapacity"]);
                    return mWh / 1000.0; // mWh → Wh
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo leer la capacidad de diseño de la batería.");
            }
            return 0;
        }

        /// <summary>Nombre del adaptador WiFi (crudo). null si no hay adaptador inalámbrico.</summary>
        private string? GetWifiAdapter()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "select Name, NetConnectionID from Win32_NetworkAdapter where PhysicalAdapter = true");
                foreach (var adapter in searcher.Get().OfType<ManagementObject>())
                {
                    string name = adapter["Name"]?.ToString() ?? "";
                    string conn = adapter["NetConnectionID"]?.ToString() ?? "";
                    string combined = (name + " " + conn).ToLowerInvariant();
                    if (combined.Contains("wi-fi") || combined.Contains("wifi") || combined.Contains("wireless")
                        || combined.Contains("802.11") || combined.Contains("wlan"))
                    {
                        return name.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al detectar el adaptador WiFi.");
            }
            return null;
        }

        /// <summary>Nombre de la cámara (crudo). Descarta cámaras virtuales. null si no hay.</summary>
        private string? GetCameraName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "select Name, PNPClass from Win32_PnPEntity where PNPClass = 'Camera' or PNPClass = 'Image'");
                foreach (var device in searcher.Get().OfType<ManagementObject>())
                {
                    string name = device["Name"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string lower = name.ToLowerInvariant();
                    // Excluye escáneres (PNPClass 'Image') y cámaras virtuales.
                    if (lower.Contains("scanner") || lower.Contains("obs") || lower.Contains("virtual")
                        || lower.Contains("droidcam") || lower.Contains("manycam"))
                        continue;
                    return name;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al detectar la cámara.");
            }
            return null;
        }

        private string GetWmiProperty(string wmiClass, string property)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"select {property} from {wmiClass}"))
                {
                    var firstObject = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                    return firstObject?[property]?.ToString() ?? "No disponible";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener propiedad WMI {Property} de la clase {WmiClass}", property, wmiClass);
                return "Error WMI";
            }
        }

        private string GetPcName() => Environment.MachineName;
        private string GetOsName() => GetWmiProperty("Win32_OperatingSystem", "Caption");
        private string GetCpuName() => GetWmiProperty("Win32_Processor", "Name").Trim();
        private string GetGpuName() => GetWmiProperty("Win32_VideoController", "Name");

        private string GetScreenResolution()
        {
            try
            {
                // Note: This needs to run on UI thread or use a different way to get resolution if not on UI thread.
                // Since this service might be called from ViewModel, we should be careful.
                // Application.Current.Dispatcher.Invoke might be needed if called from non-UI thread.
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    var width = SystemParameters.PrimaryScreenWidth;
                    var height = SystemParameters.PrimaryScreenHeight;
                    return $"{width} x {height}";
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener la resolución de pantalla.");
                return "Desconocida";
            }
        }

        private string GetCpuCores()
        {
            var cores = GetWmiProperty("Win32_Processor", "NumberOfCores");
            var threads = GetWmiProperty("Win32_Processor", "NumberOfLogicalProcessors");
            return $"{cores} Núcleos / {threads} Hilos";
        }

        private string GetRamDetails()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("select * from Win32_PhysicalMemory");
                var modules = searcher.Get().OfType<ManagementObject>().ToList();

                ulong totalBytes = 0;
                foreach (var module in modules)
                {
                    totalBytes += (ulong)module["Capacity"];
                }

                double totalGB = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 1);

                var firstModule = modules.FirstOrDefault();
                if (firstModule != null)
                {
                    string manufacturer = (firstModule["Manufacturer"]?.ToString() ?? "Desconocido").Trim();
                    string speed = (firstModule["Speed"]?.ToString() ?? "N/A").Trim();
                    string partNumber = (firstModule["PartNumber"]?.ToString() ?? "").Trim();

                    if (manufacturer == "0x80AD") manufacturer = "Hynix";
                    if (manufacturer == "0x802C") manufacturer = "Micron";
                    if (manufacturer == "0x0198") manufacturer = "Kingston";
                    if (manufacturer == "0x830B") manufacturer = "Samsung";

                    string ddr = GetDdrGeneration(firstModule, speed);
                    string gen = string.IsNullOrEmpty(ddr) ? "" : $" {ddr}";
                    // Formato: "32 GB DDR5 (Kingston KF... @ 4800 MHz)" → el formatter parte titular/técnico.
                    return $"{totalGB} GB{gen} ({manufacturer} {partNumber} @ {speed} MHz)";
                }

                return $"{totalGB} GB";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener detalles de la RAM");
                return "RAM No disponible";
            }
        }

        /// <summary>Generación DDR ("DDR5"/"DDR4"...). Usa SMBIOSMemoryType y, si no, infiere por velocidad.</summary>
        private string GetDdrGeneration(ManagementObject module, string speedText)
        {
            try
            {
                if (module["SMBIOSMemoryType"] != null)
                {
                    int code = Convert.ToInt32(module["SMBIOSMemoryType"]);
                    string mapped = code switch
                    {
                        34 => "DDR5",
                        26 => "DDR4",
                        24 => "DDR3",
                        21 => "DDR2",
                        _ => ""
                    };
                    if (mapped.Length > 0) return mapped;
                }
            }
            catch { /* cae al heurístico por velocidad */ }

            // Heurístico por velocidad (MT/s) cuando el tipo SMBIOS no está disponible.
            if (int.TryParse(speedText, out int mhz))
            {
                if (mhz >= 4000) return "DDR5";
                if (mhz >= 2100) return "DDR4";
                if (mhz >= 1066) return "DDR3";
            }
            return "";
        }

        private string GetStorageDetails()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                var driveStrings = new List<string>();

                foreach (ManagementObject mo in searcher.Get())
                {
                    string model = (mo["Model"]?.ToString() ?? "Disco Genérico").Trim();
                    ulong totalBytes = (ulong)mo["Size"];
                    double totalGB = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 0);

                    string mediaType = GetMediaTypeString(mo["MediaType"]?.ToString() ?? "", model);

                    if (model.Contains("WDC PC SN530"))
                    {
                        model = "Western Digital SN530";
                    }

                    driveStrings.Add($"{model} ({totalGB} GB {mediaType})");
                }

                if (driveStrings.Count == 1) return driveStrings[0];
                return string.Join(" + ", driveStrings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener detalles del almacenamiento");
                return "Almacenamiento No disponible";
            }
        }

        private string GetMediaTypeString(string mediaTypeCode, string modelName)
        {
            if (!string.IsNullOrEmpty(mediaTypeCode))
            {
                if (mediaTypeCode == "4" || mediaTypeCode.Contains("Solid State")) return "SSD";
                if (mediaTypeCode == "3" || mediaTypeCode.Contains("Hard Disk")) return "HDD";
            }

            if (!string.IsNullOrEmpty(modelName))
            {
                string upperModel = modelName.ToUpper();
                if (upperModel.Contains("SSD") || upperModel.Contains("NVME") || upperModel.Contains("SN530"))
                {
                    return "SSD";
                }
            }

            return "Desconocido";
        }
    }
}