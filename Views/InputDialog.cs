using System.Windows;
using System.Windows.Controls;

namespace CaptionScribe.Views
{
    /// <summary>Minimal modal text prompt (WPF has no built-in input box).</summary>
    internal sealed class InputDialog : Window
    {
        private readonly TextBox _input;

        public string ResponseText => _input.Text;

        public InputDialog(string prompt, string title, string defaultText = "")
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });

            _input = new TextBox { Text = defaultText };
            root.Children.Add(_input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };
            var style = Application.Current?.TryFindResource("DialogButton") as Style;
            var ok = new Button { Content = "OK", Style = style, MinWidth = 92, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Style = style, MinWidth = 92, IsCancel = true };
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = root;
            Loaded += (_, _) => { _input.SelectAll(); _input.Focus(); };
        }
    }
}
