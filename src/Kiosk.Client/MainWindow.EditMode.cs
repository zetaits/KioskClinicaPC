using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using KioskClinicaPC.Controls;
using KioskClinicaPC.Core;
using KioskClinicaPC.Windows;

namespace KioskClinicaPC
{
    /// <summary>
    /// Parte de <see cref="MainWindow"/> con el modo de edición libre: entrar/salir, guardar/descartar,
    /// navegación entre pantallas durante la edición y los pasos de slides/cuotas. Separado del
    /// code-behind principal por ser un sub-modo autocontenido (la barra flotante de edición).
    /// </summary>
    public partial class MainWindow
    {
        private void EnterEditMode()
        {
            // Con servidor de contenido, lo editable inline (slides/textos/marketing) es compartido y lo
            // gestiona el panel; editarlo local se perdería en el siguiente merge. Se bloquea la entrada.
            if (!string.IsNullOrWhiteSpace(_settings.ServerUrl))
            {
                KioskDialog.Alert(this, "Contenido gestionado",
                    "Este kiosko recibe el contenido compartido (slides, textos, marketing) del servidor y se edita en el panel. " +
                    "El precio y las especificaciones de este equipo se editan en Ajustes.");
                return;
            }

            _timers.Stop(KioskTimer.Inactivity);
            _timers.Stop(KioskTimer.AttractAdvance);
            _timers.Stop(KioskTimer.AutoScan);
            _timers.Stop(KioskTimer.Highlight);
            _timers.Stop(KioskTimer.DetailAdvance);
            _autoTour = false;
            foreach (var s in _viewModel.Specs) s.IsHighlighted = false;

            EditModeService.Instance.IsDirty = false;
            EditModeService.Instance.IsActive = true;
            RefreshEditHighlights();
        }

        private void ExitEditMode()
        {
            InlineEditController.CommitActive();
            InlineEditController.SetHighlights(RootGrid, false);
            EditModeService.Instance.IsActive = false;

            if (_viewModel.CurrentScreen == 0) { EnterAttractMode(); }
            else { _timers.Start(KioskTimer.Inactivity); }
            if (_viewModel.CurrentScreen == 2)
            {
                _highlightIndex = 0;
                ApplyHighlight(0, animateMorph: false);
                _mainShown = 1;
                _timers.Start(KioskTimer.Highlight);
            }

            RefreshQr(); // los datos editados (precio, specs) pueden haber cambiado
        }

        private void RefreshEditHighlights()
        {
            if (!EditModeService.Instance.IsActive) return;
            Dispatcher.BeginInvoke(new Action(() => InlineEditController.SetHighlights(RootGrid, true)), DispatcherPriority.Loaded);
        }

        private void EditSave_Click(object sender, RoutedEventArgs e)
        {
            InlineEditController.CommitActive();
            try
            {
                _viewModel.SaveEdits();
                RefreshEditHighlights();
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"No se pudieron guardar los cambios.\n{ex.Message}", danger: true);
            }
        }

        private void EditDiscard_Click(object sender, RoutedEventArgs e)
        {
            InlineEditController.CancelActive();
            _viewModel.DiscardEdits();
            if (_viewModel.CurrentScreen == 3) NavigateToScreen(2);
            _attractSlideIndex = 0;
            UpdateSlideDots(0);
            RefreshEditHighlights();
        }

        private void EditExit_Click(object sender, RoutedEventArgs e)
        {
            if (EditModeService.Instance.IsDirty)
            {
                var r = KioskDialog.Show(this, "Modo edición", "Tienes cambios sin guardar.",
                    primaryText: "Guardar y salir", secondaryText: "Salir sin guardar", cancelText: "Cancelar");
                if (r == KioskDialogResult.Cancel) return;
                if (r == KioskDialogResult.Primary)
                {
                    InlineEditController.CommitActive();
                    try { _viewModel.SaveEdits(); }
                    catch (Exception ex)
                    {
                        KioskDialog.Alert(this, "Error", $"No se pudieron guardar los cambios.\n{ex.Message}", danger: true);
                        return;
                    }
                }
                else
                {
                    InlineEditController.CancelActive();
                    _viewModel.DiscardEdits();
                    if (_viewModel.CurrentScreen == 3) NavigateToScreen(2);
                }
            }
            ExitEditMode();
        }

        private void EditPrevSlide_Click(object sender, RoutedEventArgs e) => StepSlide(-1);
        private void EditNextSlide_Click(object sender, RoutedEventArgs e) => StepSlide(1);

        // Switch de plazo de cuotas: cada segmento fija 6 o 12 meses.
        private void Installments6_Click(object sender, RoutedEventArgs e) => _viewModel.SetInstallments(6);
        private void Installments12_Click(object sender, RoutedEventArgs e) => _viewModel.SetInstallments(12);

        private void StepSlide(int dir)
        {
            if (_viewModel.Slides.Count == 0) return;
            int idx = _viewModel.Slides.IndexOf(_viewModel.CurrentSlide);
            if (idx < 0) idx = 0;
            idx = (idx + dir + _viewModel.Slides.Count) % _viewModel.Slides.Count;
            _viewModel.CurrentSlide = _viewModel.Slides[idx];
            _attractSlideIndex = idx;
            UpdateSlideDots(idx);
            RefreshEditHighlights();
        }

        // Navegación entre pantallas dentro del modo edición (sin estos botones el usuario
        // queda atascado en la pantalla activa y no puede llegar a editar las demás).
        private void EditGoAttract_Click(object sender, RoutedEventArgs e) => GoToScreenInEdit(0);
        private void EditGoScan_Click(object sender, RoutedEventArgs e) => GoToScreenInEdit(1);
        private void EditGoMain_Click(object sender, RoutedEventArgs e) => GoToScreenInEdit(2);
        private void EditGoDetail_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectedSpec ??= _viewModel.Specs.FirstOrDefault();
            GoToScreenInEdit(3);
        }

        private void GoToScreenInEdit(int target)
        {
            if (_viewModel.CurrentScreen == target) { RefreshEditHighlights(); return; }
            NavigateToScreen(target);
        }
    }
}
