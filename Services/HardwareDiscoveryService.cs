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
            // SystemParameters debe leerse en el hilo de UI. Captúralo ANTES de Task.Run: si se
            // leyera dentro vía Dispatcher.Invoke desde un hilo del pool, un .Result/.Wait() del
            // llamador provocaría deadlock del hilo de UI.
            string screen = GetScreenResolution();
            await Task.Run(() =>
            {
                config.Cpu = GetCpuName();
                config.Cores = GetCpuCores();
                var ram = GetRamDetails();
                config.Ram = ram.Value;
                config.RamDetail = ram.Detail;
                config.Gpu = GetGpuName();
                var storage = GetStorageDetails();
                config.Storage = storage.Value;
                config.StorageDetail = storage.Detail;
                config.Screen = screen;
                config.ScreenDetail = GetScreenDetail(screen);
                config.Os = $"{GetOsName()} ({GetPcName()})";

                // Identidad real del equipo (sustituye el hardcode "ASUS ROG").
                config.ChassisName = GetManufacturer();
                config.ModelName = GetModel();
                config.Sku = GetSku();
                config.Family = GetChassisFamily();

                // Componentes opcionales: null si el equipo no los tiene (no se muestran).
                var battery = GetBatteryInfo();
                config.Battery = battery.Value;
                config.BatteryDetail = battery.Detail;
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

        /// <summary>Capacidad/presencia de batería. Value null en sobremesa (sin batería).
        /// Detail = química de la celda (Li-ion / Li-polímero) si WMI la reporta.</summary>
        private (string? Value, string? Detail) GetBatteryInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select EstimatedChargeRemaining, Chemistry from Win32_Battery");
                var battery = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                if (battery == null) return (null, null); // sobremesa: sin batería

                // Capacidad de diseño en Wh desde root\wmi (más fiable que Win32_Battery).
                double wh = GetBatteryDesignWh();
                string value = wh > 0 ? $"{Math.Round(wh)} Wh" : "Batería integrada";

                string? detail = null;
                // Win32_Battery.Chemistry: 6 = Li-ion, 8 = Li-polímero (resto poco común en portátiles).
                if (battery["Chemistry"] != null && int.TryParse(battery["Chemistry"].ToString(), out int chem))
                {
                    detail = chem switch { 6 => "Li-ion", 8 => "Li-polímero", _ => null };
                }
                return (value, detail);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al detectar la batería.");
                return (null, null);
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

        // Palabras que delatan que un dispositivo NO es una webcam real: impresoras/escáneres
        // multifunción (que se registran bajo PNPClass 'Image') y cámaras virtuales.
        private static readonly string[] NonCameraKeywords =
        {
            "scanner", "scan", "printer", "fax", "mfp", "multifunction",
            "obs", "virtual", "droidcam", "manycam", "splitcam", "xsplit", "epoccam",
            // Marcas/series típicas de impresora-escáner multifunción (PNPClass 'Image').
            "brother", "epson", "canon", "officejet", "deskjet", "pixma", "imageclass",
            "ecotank", "workforce", "laserjet", "dcp-", "mfc-",
        };

        // Señales positivas de que un dispositivo PNPClass 'Image' (clase heredada y ambigua)
        // es realmente una cámara y no un escáner/impresora.
        private static readonly string[] CameraKeywords =
        {
            "cam", "webcam", "facetime", "integrated camera", "uvc", "video",
        };

        /// <summary>Nombre de la cámara (crudo). Descarta escáneres/impresoras y cámaras virtuales. null si no hay.</summary>
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
                    string pnpClass = device["PNPClass"]?.ToString()?.Trim() ?? "";
                    string lower = name.ToLowerInvariant();

                    // Denylist: descarta escáneres/impresoras multifunción y cámaras virtuales.
                    if (NonCameraKeywords.Any(k => lower.Contains(k)))
                    {
                        Log.Debug("Descartado como cámara (denylist): {Name} [{Class}]", name, pnpClass);
                        continue;
                    }

                    // PNPClass 'Camera' es fiable. 'Image' es la clase heredada y ambigua
                    // (escáneres, faxes, MFP): solo se acepta con señal positiva de cámara.
                    if (pnpClass.Equals("Image", StringComparison.OrdinalIgnoreCase)
                        && !CameraKeywords.Any(k => lower.Contains(k)))
                    {
                        Log.Debug("PNPClass 'Image' sin señal de cámara, descartado: {Name}", name);
                        continue;
                    }

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
        /// <summary>
        /// Devuelve la GPU dedicada si existe (NVIDIA o AMD discreta), no la integrada.
        /// Win32_VideoController lista todas las GPU instaladas; en portátiles híbridos
        /// (Optimus) hay iGPU + dedicada y el orden WMI no es fiable, así que filtramos.
        /// </summary>
        private string GetGpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "select Name, AdapterCompatibility from Win32_VideoController");
                var gpus = searcher.Get().OfType<ManagementObject>()
                    .Select(o => new {
                        Name = o["Name"]?.ToString() ?? "",
                        Vendor = o["AdapterCompatibility"]?.ToString() ?? ""
                    })
                    .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                    .ToList();

                if (gpus.Count == 0) return "No disponible";

                bool IsIntegrated(string n) =>
                    n.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Vega", StringComparison.OrdinalIgnoreCase) ||
                    n.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase); // iGPU AMD

                var dedicated = gpus.FirstOrDefault(g =>
                    g.Vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    (g.Vendor.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) && !IsIntegrated(g.Name)));

                return (dedicated ?? gpus[0]).Name;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al detectar la GPU.");
                return "Error WMI";
            }
        }

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

        private (string Value, string? Detail) GetRamDetails()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_PhysicalMemory");
                using var collection = searcher.Get();
                var modules = collection.OfType<ManagementObject>().ToList();
                try
                {
                    ulong totalBytes = 0;
                    int moduleCount = 0;
                    foreach (var module in modules)
                    {
                        // Algunos BIOS OEM no reportan Capacity: saltar ese módulo en vez de
                        // abortar (un null desboxeado tiraba toda la detección de RAM).
                        if (module["Capacity"] is null) continue;
                        totalBytes += Convert.ToUInt64(module["Capacity"]);
                        moduleCount++;
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
                        string value = $"{totalGB} GB{gen} ({manufacturer} {partNumber} @ {speed} MHz)";

                        // Detalle real (StatStrip): velocidad · generación · nº de módulos. Sin claims inventados.
                        var tokens = new List<string>();
                        if (!string.IsNullOrEmpty(speed) && speed != "N/A") tokens.Add($"{speed} MHz");
                        if (!string.IsNullOrEmpty(ddr)) tokens.Add(ddr);
                        if (moduleCount > 0) tokens.Add(moduleCount == 1 ? "1 módulo" : $"{moduleCount} módulos");
                        string? detail = tokens.Count > 0 ? string.Join(" · ", tokens) : null;

                        return (value, detail);
                    }

                    return ($"{totalGB} GB", null);
                }
                finally
                {
                    foreach (var m in modules) m.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener detalles de la RAM");
                return ("RAM No disponible", null);
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

        // Tamaños comerciales (GB nominales). El espacio útil real en GiB siempre es
        // algo menor (un SSD de 512 GB reporta ~477), así que ajustamos al estándar
        // más cercano para mostrar cifras de venta limpias.
        private static readonly double[] CommercialSizesGB =
            { 64, 128, 256, 512, 1024, 2048, 4096, 8192 };

        private static double NormalizeToCommercialGB(double rawGiB)
        {
            if (rawGiB <= 0) return rawGiB;
            double best = rawGiB;
            double bestDist = double.MaxValue;
            foreach (double std in CommercialSizesGB)
            {
                double dist = Math.Abs(std - rawGiB);
                if (dist < bestDist) { bestDist = dist; best = std; }
            }
            // Solo ajustar si el estándar más cercano está dentro del ~25% relativo;
            // si no, conservar el valor real (discos no estándar, p.ej. 3 TB).
            return bestDist <= rawGiB * 0.25 ? best : rawGiB;
        }

        private (string Value, string? Detail) GetStorageDetails()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                using var collection = searcher.Get();
                var driveStrings = new List<string>();
                var detailTokens = new List<string>();

                foreach (ManagementObject mo in collection)
                {
                    using (mo)
                    {
                        // Size es null en lectores de tarjetas / unidades vacías: saltar esa fila
                        // en vez de abortar la enumeración (un disco malo ocultaba los buenos).
                        if (mo["Size"] is null) continue;

                        string model = (mo["Model"]?.ToString() ?? "Disco Genérico").Trim();
                        ulong totalBytes = Convert.ToUInt64(mo["Size"]);
                        double totalGB = NormalizeToCommercialGB(
                            Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 0));

                        string mediaType = GetMediaTypeString(mo["MediaType"]?.ToString() ?? "", model);

                        if (model.Contains("WDC PC SN530"))
                        {
                            model = "Western Digital SN530";
                        }

                        driveStrings.Add($"{model} ({totalGB} GB {mediaType})");

                        // Detalle real (StatStrip): "NVMe · 1 TB" por unidad. NVMe se distingue por modelo.
                        string ifaceType = model.ToUpperInvariant().Contains("NVME") ? "NVMe" : mediaType;
                        if (ifaceType.Equals("Desconocido", StringComparison.OrdinalIgnoreCase)) ifaceType = "";
                        string size = totalGB >= 1000
                            ? $"{Math.Round(totalGB / 1024.0, totalGB % 1024 == 0 ? 0 : 1)} TB"
                            : $"{totalGB} GB";
                        detailTokens.Add(string.IsNullOrEmpty(ifaceType) ? size : $"{ifaceType} {size}");
                    }
                }

                string value = driveStrings.Count == 1 ? driveStrings[0] : string.Join(" + ", driveStrings);
                string? detail = detailTokens.Count > 0 ? string.Join(" · ", detailTokens) : null;
                return (value, detail);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener detalles del almacenamiento");
                return ("Almacenamiento No disponible", null);
            }
        }

        /// <summary>Detalle real de pantalla: "2560×1600 · 165 Hz". Hz best-effort (omitido si no se lee).</summary>
        private string? GetScreenDetail(string resolution)
        {
            var tokens = new List<string>();
            var m = System.Text.RegularExpressions.Regex.Match(resolution ?? "", @"(?<w>\d{3,5})\D+(?<h>\d{3,5})");
            if (m.Success) tokens.Add($"{m.Groups["w"].Value}×{m.Groups["h"].Value}");

            int hz = GetScreenRefreshHz();
            if (hz > 0) tokens.Add($"{hz} Hz");

            return tokens.Count > 0 ? string.Join(" · ", tokens) : null;
        }

        /// <summary>Frecuencia de refresco del monitor principal (Hz) vía Win32_VideoController. 0 si no se lee.</summary>
        private int GetScreenRefreshHz()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "select CurrentRefreshRate from Win32_VideoController where CurrentRefreshRate is not null");
                foreach (var ctrl in searcher.Get().OfType<ManagementObject>())
                {
                    if (ctrl["CurrentRefreshRate"] != null && int.TryParse(ctrl["CurrentRefreshRate"].ToString(), out int hz) && hz > 0)
                        return hz;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener la frecuencia de refresco.");
            }
            return 0;
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