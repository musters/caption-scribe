using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using CaptionScribe.Models;
using CaptionScribe.Services;
using WinForms = System.Windows.Forms;

namespace CaptionScribe.Views
{
    /// <summary>Region selector and highlight overlay implementation.</summary>
    public sealed class RegionService : IRegionService, IDisposable
    {
        private const int HighlightDurationMs = 2500;
        private RegionHighlightForm? _overlay;

        public CaptureRegion? SelectRegion()
        {
            // App windows are top-most and would obscure the screen being selected (a problem on a single
            // monitor), so hide the visible ones during selection and restore exactly those afterwards.
            var hidden = HideVisibleWindows();
            try
            {
                using var selector = new RegionSelectorForm();
                return selector.ShowDialog() == WinForms.DialogResult.OK ? selector.SelectedRegion : null;
            }
            finally
            {
                foreach (var window in hidden)
                    window.Show();
            }
        }

        public void HighlightRegion(CaptureRegion region)
        {
            _overlay?.Close();
            _overlay = new RegionHighlightForm(region, HighlightDurationMs);
            _overlay.Show();
        }

        public void Dispose() => _overlay?.Dispose();

        private static List<Window> HideVisibleWindows()
        {
            var hidden = new List<Window>();
            if (Application.Current is not { } app)
                return hidden;

            foreach (Window window in app.Windows)
            {
                if (!window.IsVisible)
                    continue;
                window.Hide();
                hidden.Add(window);
            }

            if (hidden.Count > 0)
                // Let the windows actually leave the screen before the selection overlay appears.
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            return hidden;
        }
    }
}
