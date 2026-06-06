using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KioskClinicaPC.Core
{
    public class AppConfig : INotifyPropertyChanged
    {
        /// <summary>Versión del esquema de configuración actual. Súbela al cambiar la forma del
        /// JSON (renombrar/mover/cambiar tipos) y añade el paso correspondiente en ConfigMigrator.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Versión del esquema con que se guardó este archivo. Los archivos previos al
        /// versionado no la traen → se deserializa como 0 y ConfigMigrator la actualiza.</summary>
        public int SchemaVersion { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private string? _price;
        public string? Price { get => _price; set => SetProperty(ref _price, value); }

        private string? _discountedPrice;
        public string? DiscountedPrice { get => _discountedPrice; set => SetProperty(ref _discountedPrice, value); }

        // Mostrar el distintivo "REACONDICIONADO" en la ficha. Inicializado a true: los configs antiguos
        // (sin el campo) conservan el comportamiento previo (Newtonsoft no toca propiedades ausentes).
        private bool _showRefurbished = true;
        public bool ShowRefurbished { get => _showRefurbished; set => SetProperty(ref _showRefurbished, value); }

        // Estado del equipo: "Nuevo" (garantía 3 años) u "Ocasion" (1 año). Determina la garantía mostrada.
        private string? _condition = "Ocasion";
        public string? Condition { get => _condition; set => SetProperty(ref _condition, value); }

        private string? _cpu;
        public string? Cpu { get => _cpu; set => SetProperty(ref _cpu, value); }

        private string? _cores;
        public string? Cores { get => _cores; set => SetProperty(ref _cores, value); }

        private string? _ram;
        public string? Ram { get => _ram; set => SetProperty(ref _ram, value); }

        // Detalle técnico real por componente (la fila de tokens "·" del StatStrip). null si no se detecta:
        // entonces cae al override manual de Settings y, en último término, al genérico del catálogo.
        private string? _ramDetail;
        public string? RamDetail { get => _ramDetail; set => SetProperty(ref _ramDetail, value); }

        private string? _storageDetail;
        public string? StorageDetail { get => _storageDetail; set => SetProperty(ref _storageDetail, value); }

        private string? _screenDetail;
        public string? ScreenDetail { get => _screenDetail; set => SetProperty(ref _screenDetail, value); }

        private string? _batteryDetail;
        public string? BatteryDetail { get => _batteryDetail; set => SetProperty(ref _batteryDetail, value); }

        // Detalle solo-editable (WMI no lo aporta de forma fiable): la tienda lo rellena en Settings.
        private string? _gpuDetail;
        public string? GpuDetail { get => _gpuDetail; set => SetProperty(ref _gpuDetail, value); }

        private string? _wifiDetail;
        public string? WifiDetail { get => _wifiDetail; set => SetProperty(ref _wifiDetail, value); }

        private string? _cameraDetail;
        public string? CameraDetail { get => _cameraDetail; set => SetProperty(ref _cameraDetail, value); }

        private string? _portsDetail;
        public string? PortsDetail { get => _portsDetail; set => SetProperty(ref _portsDetail, value); }

        private string? _osDetail;
        public string? OsDetail { get => _osDetail; set => SetProperty(ref _osDetail, value); }

        private string? _gpu;
        public string? Gpu { get => _gpu; set => SetProperty(ref _gpu, value); }

        private string? _storage;
        public string? Storage { get => _storage; set => SetProperty(ref _storage, value); }

        private string? _screen;
        public string? Screen { get => _screen; set => SetProperty(ref _screen, value); }

        private string? _os;
        public string? Os { get => _os; set => SetProperty(ref _os, value); }

        private string? _battery;
        public string? Battery { get => _battery; set => SetProperty(ref _battery, value); }

        private string? _wifi;
        public string? Wifi { get => _wifi; set => SetProperty(ref _wifi, value); }

        private string? _camera;
        public string? Camera { get => _camera; set => SetProperty(ref _camera, value); }

        private string? _ports;
        public string? Ports { get => _ports; set => SetProperty(ref _ports, value); }

        private string? _chassisName;
        public string? ChassisName { get => _chassisName; set => SetProperty(ref _chassisName, value); }

        private string? _modelName;
        public string? ModelName { get => _modelName; set => SetProperty(ref _modelName, value); }

        private string? _family;
        public string? Family { get => _family; set => SetProperty(ref _family, value); }

        private string? _sku;
        public string? Sku { get => _sku; set => SetProperty(ref _sku, value); }

        /// <summary>Dirección por defecto de la tienda. Fuente única: la web (docs/app.js → SHOP.address)
        /// la conoce, así que no se manda en el QR cuando coincide con este valor (lo aligera).</summary>
        public const string DefaultShopAddress = "Calle Sevilla 54, Málaga";

        private string? _shopAddress;
        public string? ShopAddress { get => _shopAddress; set => SetProperty(ref _shopAddress, value); }

        private string? _shopServices;
        public string? ShopServices { get => _shopServices; set => SetProperty(ref _shopServices, value); }

        private string? _productImagePath;
        public string? ProductImagePath { get => _productImagePath; set => SetProperty(ref _productImagePath, value); }

        public List<SpecMarketingData> MarketingData { get; set; } = new List<SpecMarketingData>();

        public List<AttractSlide> AttractSlides { get; set; } = new List<AttractSlide>();

        public Dictionary<string, string> UiTexts { get; set; } = new Dictionary<string, string>();
    }

    public class SpecMarketingData
    {
        public string? Id { get; set; }
        public string? Family { get; set; }
        public string? Label { get; set; }
        public string? Summary { get; set; }
        public int BenchScore { get; set; }
        public string? BenchLabel { get; set; }
        public List<string> Pros { get; set; } = new List<string>();
        public string? DefaultValue { get; set; }
        public string? DefaultDetail { get; set; }
    }
}
