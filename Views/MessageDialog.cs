using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CaptionScribe.Views
{
    /// <summary>Modal message box with app-styled buttons; Result is the index pressed (-1 if dismissed).</summary>
    internal sealed class MessageDialog : Window
    {
        public int Result { get; private set; } = -1;

        public MessageDialog(string title, string message,
            IReadOnlyList<(string Text, bool IsDefault, bool IsCancel)> buttons)
        {
            Title = title;
            Width = 440;
            MaxHeight = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(18) };

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            DockPanel.SetDock(bar, Dock.Bottom);

            var style = Application.Current?.TryFindResource("DialogButton") as Style;
            for (int i = 0; i < buttons.Count; i++)
            {
                var (text, isDefault, isCancel) = buttons[i];
                int index = i;
                var button = new Button
                {
                    Content = text,
                    Style = style,
                    MinWidth = 92,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = isDefault,
                    IsCancel = isCancel,
                };
                button.Click += (_, _) => { Result = index; DialogResult = true; };
                bar.Children.Add(button);
            }
            root.Children.Add(bar);

            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            });

            Content = root;
        }
    }
}
