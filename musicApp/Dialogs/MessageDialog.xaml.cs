using System.Windows;
using System.Windows.Controls;
using musicApp.Helpers;

namespace musicApp.Dialogs
{
    public partial class MessageDialog : Window
    {
        public enum Buttons
        {
            Ok,
            YesNo
        }

        /// <summary>True if OK/Yes was clicked; false if No was clicked; null if closed without choosing (e.g. Escape).</summary>
        public bool? Result { get; private set; }

        public MessageDialog()
        {
            InitializeComponent();
        }

        public static bool? Show(Window? owner, string title, string message, Buttons buttons)
        {
            var dlg = new MessageDialog
            {
                Owner = owner,
                Title = title
            };
            dlg.TxtMessage.Text = message;
            dlg.BuildButtons(buttons);
            dlg.ShowDialog();
            WindowFocusHelper.ScheduleActivate(dlg.Owner as Window);
            return dlg.Result;
        }

        private void BuildButtons(Buttons buttons)
        {
            if (buttons == Buttons.Ok)
            {
                var ok = new Button { Content = "OK", IsDefault = true };
                ok.Click += (s, e) => { Result = true; Close(); };
                ButtonPanel.Children.Add(ok);
                return;
            }
            if (buttons == Buttons.YesNo)
            {
                var yes = new Button { Content = "Yes", IsDefault = true };
                yes.Click += (s, e) => { Result = true; Close(); };
                ButtonPanel.Children.Add(yes);
                var no = new Button { Content = "No", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
                no.Click += (s, e) => { Result = false; Close(); };
                ButtonPanel.Children.Add(no);
            }
        }
    }
}
