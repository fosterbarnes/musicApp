using System.Windows;
using System.Windows.Input;

namespace musicApp.Dialogs
{
    public partial class TextInputDialog : Window
    {
        public string? Result { get; private set; }

        public TextInputDialog()
        {
            InitializeComponent();
        }

        /// <summary>Show the dialog. Returns the trimmed input text, or null if cancelled.</summary>
        public static string? Show(Window? owner, string title, string label, string defaultText = "")
        {
            var dlg = new TextInputDialog
            {
                Owner = owner,
                Title = title
            };
            dlg.TxtLabel.Text = label;
            dlg.TxtInput.Text = defaultText ?? "";
            dlg.TxtInput.SelectAll();
            dlg.TxtInput.Focus();
            dlg.ShowDialog();
            return dlg.Result;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Result = TxtInput.Text?.Trim();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
            Close();
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
