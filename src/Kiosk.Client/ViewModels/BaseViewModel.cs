using KioskClinicaPC.Core;

namespace KioskClinicaPC.ViewModels
{
    /// <summary>Base de los ViewModels. Toda la maquinaria observable vive en <see cref="ObservableObject"/>;
    /// este tipo existe para que los VM declaren intención (y poder añadir helpers solo de VM en el futuro).</summary>
    public abstract class BaseViewModel : ObservableObject
    {
    }
}
