using System.Collections.Generic;

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
        public string Battery { get; set; }
        public string Wifi { get; set; }
        public string Camera { get; set; }
        public string Ports { get; set; }

        public string ChassisName { get; set; }
        public string ModelName { get; set; }
        public string Family { get; set; }
        public string Sku { get; set; }

        public string ShopAddress { get; set; }
        public string ShopServices { get; set; }

        public List<SpecMarketingData> MarketingData { get; set; } = new List<SpecMarketingData>();
    }

    public class SpecMarketingData
    {
        public string Id { get; set; }
        public string Family { get; set; }
        public string Label { get; set; }
        public string Summary { get; set; }
        public int BenchScore { get; set; }
        public string BenchLabel { get; set; }
        public List<string> Pros { get; set; } = new List<string>();
        public string DefaultValue { get; set; }
        public string DefaultDetail { get; set; }
    }
}