using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaptionScribe.ViewModels;

namespace CaptionScribe.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>When false, closing the window hides it to the tray instead of disposing it.</summary>
        public bool AllowClose { get; set; }

        public MainWindow() => InitializeComponent();

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        // Space anywhere on the window (except buttons/menus) toggles capture.
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space)
                return;
            if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase or MenuItem)
                return;
            ViewModel?.ToggleActive();
            e.Handled = true;
        }

        // Auto-scroll follows the view: off when the user scrolls up, on again at the bottom.
        // ExtentHeightChange == 0 means the user scrolled (not new content), so only then re-evaluate.
        private void OnTranscriptScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange != 0 || ViewModel is null)
                return;
            const double tolerance = 2.0;
            ViewModel.AutoScroll = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - tolerance;
        }

        // Keep the newest text in view while auto-scroll is on.
        private void OnTranscriptTextChanged(object sender, TextChangedEventArgs e)
        {
            if (ViewModel?.AutoScroll == true)
            {
                TranscriptBox.CaretIndex = TranscriptBox.Text.Length;
                TranscriptBox.ScrollToEnd();
            }
        }

        private void OnRegionTextClick(object sender, MouseButtonEventArgs e)
            => ViewModel?.SelectRegionCommand.Execute(null);

        private void OnPlaceholderClick(object sender, MouseButtonEventArgs e)
            => ViewModel?.ToggleActive();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }
    }
}
