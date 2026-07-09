namespace KioskClinicaPC.Core.Config
{
    /// <summary>
    /// Reparto de responsabilidades entre servidor y kiosko. El servidor gestiona el contenido COMPARTIDO
    /// (identidad de tienda, slides, textos de UI, catálogo de marketing, imágenes); cada PC conserva su
    /// contenido LOCAL por-máquina (precio, estado y especificaciones autodetectadas). Así un solo panel
    /// coordina el marketing de toda la tienda sin pisar el precio/hardware propio de cada equipo.
    /// </summary>
    public static class SharedContent
    {
        /// <summary>Copia SOLO los campos compartidos del servidor sobre la config local de la máquina.
        /// Todo lo demás (precio, estado, specs) permanece como estaba en <paramref name="local"/>.</summary>
        public static void ApplyServerToLocal(AppConfig local, AppConfig server)
        {
            local.ShopAddress = server.ShopAddress;
            local.ShopServices = server.ShopServices;
            local.MarketingData = server.MarketingData;
            local.AttractSlides = server.AttractSlides;
            local.AttractSlidesNew = server.AttractSlidesNew;
            local.UiTexts = server.UiTexts;

            // El esquema lo marca el servidor: es quien versiona el contenido compartido.
            local.SchemaVersion = server.SchemaVersion;
        }
    }
}
