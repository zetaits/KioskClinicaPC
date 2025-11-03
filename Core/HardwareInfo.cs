using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Windows;

namespace KioskClinicaPC
{
    public class HardwareInfo
    {
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
            catch
            {
                return "Error WMI";
            }
        }

        public string GetPcName() => Environment.MachineName;
        public string GetOsName() => GetWmiProperty("Win32_OperatingSystem", "Caption");
        public string GetCpuName() => GetWmiProperty("Win32_Processor", "Name").Trim();
        public string GetGpuName() => GetWmiProperty("Win32_VideoController", "Name");

        public string GetScreenResolution()
        {
            var width = SystemParameters.PrimaryScreenWidth;
            var height = SystemParameters.PrimaryScreenHeight;
            return $"{width} x {height}";
        }

        public string GetCpuCores()
        {
            var cores = GetWmiProperty("Win32_Processor", "NumberOfCores");
            var threads = GetWmiProperty("Win32_Processor", "NumberOfLogicalProcessors");
            return $"{cores} Núcleos / {threads} Hilos";
        }

        public string GetRamDetails()
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
            catch
            {
                return "RAM No disponible";
            }
        }

        public string GetStorageDetails()
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
            catch
            {
                return "Almacenamiento No disponible";
            }
        }

        private string GetMediaTypeString(string mediaTypeCode, string modelName)
        {
            // 1. Comprobar el código de WMI (que a veces falla)
            if (!string.IsNullOrEmpty(mediaTypeCode))
            {
                if (mediaTypeCode == "4" || mediaTypeCode.Contains("Solid State")) return "SSD";
                if (mediaTypeCode == "3" || mediaTypeCode.Contains("Hard Disk")) return "HDD";
            }

            // 2. Fallback: Comprobar el nombre del modelo (mucho más fiable para NVMe)
            if (!string.IsNullOrEmpty(modelName))
            {
                string upperModel = modelName.ToUpper();

                // El SN530 es un NVMe SSD, así que estas comprobaciones lo detectarán
                if (upperModel.Contains("SSD") || upperModel.Contains("NVME") || upperModel.Contains("SN530"))
                {
                    return "SSD";
                }
            }

            // 3. Si todo falla
            return "Desconocido";
        }
    }
}