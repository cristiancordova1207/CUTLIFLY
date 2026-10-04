using System.Windows;
using Cutlify.Clipboard;

namespace Cutlify.UI
{
    public partial class OcrWindow : Window
    {
        public OcrWindow(string text)
        {
            InitializeComponent();
            TextBox.Text = text ?? "";
            EmptyText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
            CopyAll.Click += (_, __) => { if (TextBox.Text.Length > 0) ClipboardService.CopyText(TextBox.Text); };
            CopySelection.Click += (_, __) => { if (TextBox.SelectedText.Length > 0) ClipboardService.CopyText(TextBox.SelectedText); };
            CloseButton.Click += (_, __) => Close();
            Loaded += (_, __) => TextBox.Focus();
        }
    }
}
