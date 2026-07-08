namespace KioskClinicaPC.Services
{
    /// <summary>
    /// Diálogos modales simples desacoplados de WPF. Permite que el ViewModel pida avisos/confirmaciones
    /// sin invocar <c>MessageBox</c> directamente (lo que ataba el VM a la UI e impedía testearlo).
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Muestra un aviso (un solo botón de aceptar).</summary>
        void Warn(string message, string title);

        /// <summary>Pregunta Sí/No. Devuelve true si el usuario acepta.</summary>
        bool Confirm(string message, string title);
    }
}
