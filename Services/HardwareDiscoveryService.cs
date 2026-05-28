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
            });
            return config;
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

                    return $"{totalGB} GB ({manufacturer} {partNumber} @ {speed} MHz)";
                }

                return $"{totalGB} GB";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al obtener detalles de la RAM");
                return "RAM No disponible";
            }
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