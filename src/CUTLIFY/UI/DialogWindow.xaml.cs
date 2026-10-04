using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cutlify.UI
{
    /// <summary>Diálogo con la estética de CUTLIFY. Devuelve el índice del botón pulsado (-1 si se cierra).</summary>
    public partial class DialogWindow : Window
    {
        public int Result { get; private set; } = -1;

        public DialogWindow(string title, string message, int primary, params string[] buttons)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Content = buttons[i],
                    Margin = new Thickness(8, 0, 0, 0),
                    MinWidth = 96,
                    Style = (Style)FindResource(i == primary ? "PrimaryButton" : "OutlineButton"),
                    IsDefault = i == primary
                };
                b.Click += (_, __) => { Result = idx; Close(); };
                ButtonsPanel.Children.Add(b);
            }
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        }

        public void SetDetails(string text)
        {
            DetailsText.Text = text;
            DetailsExpander.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void SetProgress(double? percent)
        {
            ProgressPanel.Visibility = percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (percent.HasValue) Progress.Value = percent.Value;
        }

        public void SetButtonsEnabled(bool enabled) => ButtonsPanel.IsEnabled = enabled;

        public void SetMessage(string message) => MessageText.Text = message;

        public static int Ask(Window owner, string title, string message, int primary, params string[] buttons)
        {
            var d = new DialogWindow(title, message, primary, buttons);
            if (owner != null && owner.IsVisible) { d.Owner = owner; d.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            d.ShowDialog();
            return d.Result;
        }
    }
}
