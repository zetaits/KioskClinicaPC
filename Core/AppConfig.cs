namespace KioskClinicaPC.Core
{
    public class AppConfig
    {
        public string Price { get; set; }
        public string DiscountedPrice { get; set; }
        
        public string Cpu { get; set; }
        public string Cores { get; set; }
        public string Ram { get; set; }
        public string Gpu { get; set; }
        public string Storage { get; set; }
        public string Screen { get; set; }
        public string Os { get; set; }
        public string Motherboard { get; set; }
        public string PowerSupply { get; set; }
        public string Case { get; set; }

        // Definiciones para el Panel Experto (Capa 3)
        public string CpuDefinition { get; set; }
        public string RamDefinition { get; set; }
        public string GpuDefinition { get; set; }
        public string StorageDefinition { get; set; }
        public string MotherboardDefinition { get; set; }
        public string PowerSupplyDefinition { get; set; }
        public string CaseDefinition { get; set; }
        public string ScreenDefinition { get; set; }
        public string OsDefinition { get; set; }
    }
}