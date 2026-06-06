using System.Collections.Generic;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Datos de marketing por defecto (titulares, resúmenes, pros) y slides del Attract.
    /// Extraídos del ViewModel para que la "semilla" de contenido viva en un único sitio editable.
    /// Se usan solo como valores iniciales; el usuario los puede editar y se persisten en KioskConfig.json.
    ///
    /// IMPORTANTE: el detalle técnico (DefaultDetail) y la potencia ya NO se inventan aquí. El detalle
    /// real lo aporta la detección de hardware (HardwareDiscoveryService.*Detail) y, si no, un override
    /// manual en Settings; la potencia/gama la calcula <see cref="PerformanceScorer"/> sobre el equipo
    /// real. DefaultDetail queda vacío a propósito: jamás debe mostrar specs falsas iguales en todo PC.
    /// </summary>
    public static class SpecCatalog
    {
        public static List<SpecMarketingData> DefaultMarketing() => new List<SpecMarketingData>
        {
            new SpecMarketingData { Id = ComponentIds.Cpu, Family = "CPU", Label = "Procesador", Summary = "El cerebro del equipo. Más núcleos y más velocidad significan que abre programas al instante y no se atasca con varias cosas a la vez.", BenchLabel = "vs. PC de gama media", Pros = new List<string> { "Edición de vídeo sin tirones", "Multitarea pesada", "Compilar / renderizar rápido" }, DefaultValue = "Procesador", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Gpu, Family = "GPU", Label = "Tarjeta gráfica", Summary = "La pieza que dibuja en pantalla. Esta gráfica está pensada para jugar a calidad alta y para acelerar IA, edición y modelado 3D.", BenchLabel = "rendimiento gaming 1080p", Pros = new List<string> { "Juegos AAA en alto", "Streaming sin lag", "Aceleración IA local" }, DefaultValue = "Tarjeta gráfica", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Ram, Family = "RAM", Label = "Memoria RAM", Summary = "El espacio de trabajo del PC. Cuanta más RAM, más pestañas, programas y proyectos abiertos a la vez sin que vaya lento.", BenchLabel = "vs. portátil estándar (8 GB)", Pros = new List<string> { "Decenas de pestañas abiertas", "Photoshop + navegador + Discord", "Máquinas virtuales" }, DefaultValue = "Memoria RAM", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Storage, Family = "SSD", Label = "Almacenamiento", Summary = "Donde se guarda todo. Un SSD NVMe es hasta 30× más rápido que un disco duro: el PC arranca en segundos y los juegos cargan al vuelo.", BenchLabel = "vs. disco duro tradicional", Pros = new List<string> { "Arranque en <10 s", "Carga de juegos instantánea", "Transferencia de archivos rápida" }, DefaultValue = "Almacenamiento", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Screen, Family = "DISPLAY", Label = "Pantalla", Summary = "Imágenes nítidas y movimiento fluido. Una buena pantalla se nota en juegos, vídeo y en el día a día.", BenchLabel = "fluidez en juegos competitivos", Pros = new List<string> { "Juegos competitivos", "Edición de foto/vídeo en color real", "Series y películas en alta resolución" }, DefaultValue = "Pantalla", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Battery, Family = "POWER", Label = "Batería", Summary = "Una jornada de trabajo lejos del enchufe en uso normal. Para jugar se recomienda con el cargador conectado.", BenchLabel = "autonomía vs. gaming laptops", Pros = new List<string> { "Universidad / oficina", "Carga rápida USB-C", "Sin enchufe para tareas ligeras" }, DefaultValue = "Batería", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Wifi, Family = "CONECTIVIDAD", Label = "Conectividad WiFi", Summary = "La última generación de WiFi: más rápido y con menos interferencias. Bluetooth para mando, auriculares y periféricos.", BenchLabel = "vs. WiFi 5", Pros = new List<string> { "Streaming 4K sin cortes", "Partidas con ping bajo", "Latencia mínima en BT" }, DefaultValue = "Conectividad WiFi", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Camera, Family = "WEBCAM", Label = "Cámara", Summary = "Cámara integrada para videollamadas. Resolución y privacidad pensadas para el día a día.", BenchLabel = "calidad llamadas", Pros = new List<string> { "Teams / Zoom / Meet", "Privacidad por hardware", "Reconocimiento facial" }, DefaultValue = "Cámara", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Ports, Family = "I/O", Label = "Puertos", Summary = "Conecta lo que necesites sin adaptadores: monitores externos, red por cable y periféricos USB.", BenchLabel = "", Pros = new List<string> { "Dos monitores externos", "Carga por USB-C", "LAN gigabit" }, DefaultValue = "Puertos", DefaultDetail = "" },
            new SpecMarketingData { Id = ComponentIds.Os, Family = "OS", Label = "Sistema operativo", Summary = "Sistema operativo con licencia legal incluida y actualizaciones de seguridad al día.", BenchLabel = "", Pros = new List<string> { "Activado de fábrica", "Office compatible", "Soporte oficial Microsoft" }, DefaultValue = "Sistema operativo", DefaultDetail = "" }
        };

        public static List<AttractSlide> DefaultSlides() => new List<AttractSlide>
        {
            new AttractSlide { Eyebrow = "CLINICAPC · ANÁLISIS EN VIVO", Title1 = "ESTE EQUIPO", Title2 = "TE ESTÁ OBSERVANDO", Subtitle = "Conéctate · escanea · descubre cada componente en 30 segundos" },
            new AttractSlide { Eyebrow = "SIN TECNICISMOS", Title1 = "LO ENTIENDES", Title2 = "AUNQUE NO SEAS TÉCNICO", Subtitle = "Te traducimos cada spec a lenguaje de calle" },
            new AttractSlide { Eyebrow = "REACONDICIONADOS CON CABEZA", Title1 = "HASTA 60% MENOS", Title2 = "QUE COMPRARLO NUEVO", Subtitle = "Probado, limpiado y con 24 meses de garantía" }
        };
    }
}
