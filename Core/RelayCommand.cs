using System;
using System.Windows.Input;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// <see cref="ICommand"/> genérico para enlazar acciones del ViewModel a la UI sin handlers en
    /// el code-behind. Permite mover los <c>_Click</c> a comandos testeables. Usa
    /// <see cref="CommandManager.RequerySuggested"/> para reevaluar <c>CanExecute</c> automáticamente.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            _execute = _ => execute();
            _canExecute = canExecute == null ? null : _ => canExecute();
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
