using System.Windows.Input;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public sealed class KioskKeyPolicyTests
    {
        [Theory]
        [InlineData(Key.A)]
        [InlineData(Key.Enter)]
        [InlineData(Key.Space)]
        public void TeclaNormalEnDetalle_VuelveAlResumenInclusoSiUnBotonLaUsaria(Key key)
        {
            Assert.Equal(
                KioskKeyAction.GoToMain,
                KioskKeyPolicy.Resolve(currentScreen: 3, editMode: false, key, ModifierKeys.None));
        }

        [Theory]
        [InlineData(Key.A)]
        [InlineData(Key.Enter)]
        [InlineData(Key.Space)]
        public void TeclaNormalEnAttract_IniciaEscaneo(Key key)
        {
            Assert.Equal(
                KioskKeyAction.StartScan,
                KioskKeyPolicy.Resolve(currentScreen: 0, editMode: false, key, ModifierKeys.None));
        }

        [Fact]
        public void EscapeDesdeUnaPantallaActiva_VuelveAAttract()
        {
            Assert.Equal(
                KioskKeyAction.GoToAttract,
                KioskKeyPolicy.Resolve(currentScreen: 3, editMode: false, Key.Escape, ModifierKeys.None));
        }

        [Theory]
        [InlineData(Key.K, KioskKeyAction.Shutdown)]
        [InlineData(Key.S, KioskKeyAction.OpenSettings)]
        [InlineData(Key.P, KioskKeyAction.ToggleWindowsKey)]
        public void AtajosAdministrativos_SeConservan(Key key, KioskKeyAction expected)
        {
            Assert.Equal(
                expected,
                KioskKeyPolicy.Resolve(
                    currentScreen: 2,
                    editMode: false,
                    key,
                    ModifierKeys.Control | ModifierKeys.Shift));
        }

        [Fact]
        public void ModoEdicion_NoInterceptaLaEscritura()
        {
            Assert.Equal(
                KioskKeyAction.None,
                KioskKeyPolicy.Resolve(currentScreen: 3, editMode: true, Key.Space, ModifierKeys.None));
        }

        [Fact]
        public void TeclaEnMain_SoloCuentaComoActividad()
        {
            Assert.Equal(
                KioskKeyAction.None,
                KioskKeyPolicy.Resolve(currentScreen: 2, editMode: false, Key.Enter, ModifierKeys.None));
        }
    }
}
