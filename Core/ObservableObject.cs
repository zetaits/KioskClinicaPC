using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Base observable única del proyecto: implementa <see cref="INotifyPropertyChanged"/> una sola vez.
    /// Antes cada modelo, VM y servicio reimplementaba el patrón a mano (boilerplate divergente y
    /// propenso a olvidos). Heredar de aquí da <see cref="SetProperty{T}"/> y <see cref="OnPropertyChanged"/>.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Notifica el cambio de una propiedad (o de una propiedad calculada dependiente).</summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>Asigna el campo y notifica solo si el valor cambió. Devuelve true si hubo cambio.</summary>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
