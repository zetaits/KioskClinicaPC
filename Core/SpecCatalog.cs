using System.Collections.Generic;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Datos de marketing por defecto (titulares, resúmenes, pros, benchmarks) y slides del Attract.
    /// Extraídos del ViewModel para que la "semilla" de contenido viva en un único sitio editable.
    /// Se usan solo como valores iniciales; el usuario los puede editar y se persisten en KioskConfig.json.
    /// </summary>
    public static class SpecCatalog
    {
        public static List<SpecMarketingData> DefaultMarketing() => new List<SpecMarketingData>
        {
            new SpecMarketingData { Id = ComponentIds.Cpu, Family = "CPU", Label = "Procesador", Summary = "El cerebro del equipo. Más núcleos y más velocidad significan que abre programas al instante y no se atasca con varias cosas a la vez.", BenchScore = 86, BenchLabel = "vs. PC de gama media", Pros = new List<string> { "Edición de vídeo sin tirones", "Multitarea pesada", "Compilar / renderizar rápido" }, DefaultValue = "Intel Core i7", DefaultDetail = "13650HX · 14 núcleos · hasta 4.9 GHz" },
            new SpecMarketingData { Id = ComponentIds.Gpu, Family = "GPU", Label = "Tarjeta gráfica", Summary = "La pieza que dibuja en pantalla. Esta gráfica está pensada para jugar a calidad alta y para acelerar IA, edición y modelado 3D.", BenchScore = 92, BenchLabel = "rendimiento gaming 1080p", Pros = new List<string> { "Juegos AAA en alto", "Streaming sin lag", "Aceleración IA local" }, DefaultValue = "NVIDIA RTX 4060", DefaultDetail = "8 GB GDDR6 · Ray Tracing · DLSS 3.5" },
            new SpecMarketingData { Id = ComponentIds.Ram, Family = "RAM", Label = "Memoria RAM", Summary = "El espacio de trabajo del PC. Cuanta más RAM, más pestañas, programas y proyectos abiertos a la vez sin que vaya lento.", BenchScore = 90, BenchLabel = "vs. portátil estándar (8 GB)", Pros = new List<string> { "Decenas de pestañas abiertas", "Photoshop + navegador + Discord", "Máquinas virtuales" }, DefaultValue = "32 GB DDR5", DefaultDetail = "4800 MHz · 2× SO-DIMM · ampliable a 64 GB" },
            new SpecMarketingData { Id = ComponentIds.Storage, Family = "SSD", Label = "Almacenamiento", Summary = "Donde se guarda todo. Un SSD NVMe es hasta 30× más rápido que un disco duro: el PC arranca en segundos y los juegos cargan al vuelo.", BenchScore = 95, BenchLabel = "vs. disco duro tradicional", Pros = new List<string> { "Arranque en <10 s", "Carga de juegos instantánea", "Transferencia de archivos rápida" }, DefaultValue = "1 TB NVMe", DefaultDetail = "PCIe 4.0 · 7000 MB/s lectura · M.2 2280" },
            new SpecMarketingData { Id = ComponentIds.Screen, Family = "DISPLAY", Label = "Pantalla", Summary = "240 imágenes por segundo en alta resolución. Todo se ve nítido y el movimiento es mucho más suave que en una pantalla normal.", BenchScore = 88, BenchLabel = "fluidez en juegos competitivos", Pros = new List<string> { "Juegos competitivos", "Edición de foto/vídeo en color real", "Series y películas QHD" }, DefaultValue = "16″ QHD 240 Hz", DefaultDetail = "2560 × 1600 · IPS · 100% DCI-P3 · G-Sync" },
            new SpecMarketingData { Id = ComponentIds.Battery, Family = "POWER", Label = "Batería", Summary = "Una jornada de trabajo lejos del enchufe en uso normal. Para jugar se recomienda con el cargador conectado.", BenchScore = 70, BenchLabel = "autonomía vs. gaming laptops", Pros = new List<string> { "Universidad / oficina", "Carga rápida USB-C", "Sin enchufe para tareas ligeras" }, DefaultValue = "90 Wh", DefaultDetail = "Hasta 8 h uso ofimática · carga rápida 65 %/30 min" },
            new SpecMarketingData { Id = ComponentIds.Wifi, Family = "CONECTIVIDAD", Label = "Conectividad WiFi", Summary = "La última generación de WiFi: hasta 3× más rápido y con menos interferencias. Bluetooth 5.3 para mando, auriculares y periféricos.", BenchScore = 84, BenchLabel = "vs. WiFi 5", Pros = new List<string> { "Streaming 4K sin cortes", "Partidas con ping bajo", "Latencia mínima en BT" }, DefaultValue = "WiFi 6E + BT 5.3", DefaultDetail = "Tri-banda 6 GHz · Bluetooth 5.3 · 2.5 GbE" },
            new SpecMarketingData { Id = ComponentIds.Camera, Family = "WEBCAM", Label = "Cámara", Summary = "Resolución Full HD para videollamadas con buena cara. Lleva tapa física, así que cuando no la usas estás 100% privado.", BenchScore = 65, BenchLabel = "calidad llamadas", Pros = new List<string> { "Teams / Zoom / Meet", "Privacidad por hardware", "Reconocimiento facial" }, DefaultValue = "1080p FHD", DefaultDetail = "Con obturador de privacidad · micrófono dual" },
            new SpecMarketingData { Id = ComponentIds.Ports, Family = "I/O", Label = "Puertos", Summary = "Todo lo que necesitas conectar sin adaptadores: dos monitores externos por HDMI/USB-C, red por cable, periféricos USB-A clásicos.", BenchScore = 0, BenchLabel = "", Pros = new List<string> { "Dos monitores externos", "Carga por USB-C", "LAN gigabit" }, DefaultValue = "USB-C × 2 · HDMI 2.1", DefaultDetail = "Thunderbolt 4 · USB-A × 3 · RJ-45 · jack 3.5" },
            new SpecMarketingData { Id = ComponentIds.Os, Family = "OS", Label = "Sistema operativo", Summary = "Sistema operativo más reciente, con todas las actualizaciones de seguridad al día y licencia legal incluida.", BenchScore = 0, BenchLabel = "", Pros = new List<string> { "Activado de fábrica", "Office compatible", "Soporte oficial Microsoft" }, DefaultValue = "Windows 11 Home", DefaultDetail = "Licencia original · activada · 64-bit" }
        };

        public static List<AttractSlide> DefaultSlides() => new List<AttractSlide>
        {
            new AttractSlide { Eyebrow = "CLINICAPC · ANÁLISIS EN VIVO", Title1 = "ESTE EQUIPO", Title2 = "TE ESTÁ OBSERVANDO.", Subtitle = "Conéctate · escanea · descubre cada componente en 30 segundos." },
            new AttractSlide { Eyebrow = "SIN TECNICISMOS", Title1 = "LO ENTIENDES", Title2 = "AUNQUE NO SEAS TÉCNICO.", Subtitle = "Te traducimos cada spec a lenguaje de calle." },
            new AttractSlide { Eyebrow = "REACONDICIONADOS CON CABEZA", Title1 = "HASTA 60% MENOS", Title2 = "QUE COMPRARLO NUEVO.", Subtitle = "Probado, limpiado y con 24 meses de garantía." }
        };
    }
}
