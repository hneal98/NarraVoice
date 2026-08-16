using System.Windows;

namespace NarraVoice.UI.Dialogs
{
    public partial class InstructDialog : Window
    {
        public string InstructText { get; private set; } = "";

        public InstructDialog(string currentValue)
        {
            InitializeComponent();
            InstructTextBox.Text = currentValue;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            InstructText = InstructTextBox.Text.Trim();
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}