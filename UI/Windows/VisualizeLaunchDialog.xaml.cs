// VisualizeLaunchDialog.xaml.cs
// Two-button launcher for the audio visualizer.
// Preview — analyzes the current preview WAV.
// Other   — prompts for any MP3 or WAV file.

using System.Windows;
using Microsoft.Win32;

namespace NarraVoice.UI.Windows
{
    public partial class VisualizeLaunchDialog : Window
    {
        public string? SelectedPath { get; private set; }

        private readonly string _previewPath;

        public VisualizeLaunchDialog(string previewPath, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _previewPath = previewPath;
        }

        public void SetPreviewAvailable(bool available)
        {
            PreviewButton.IsEnabled = available;
            if (!available)
                PreviewButton.ToolTip = "No preview file found — render a preview first.";
        }

        private void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            SelectedPath = _previewPath;
            DialogResult = true;
        }

        private void OnOtherClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Audio File",
                Filter = "Audio Files (*.mp3;*.wav)|*.mp3;*.wav",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                SelectedPath = dialog.FileName;
                DialogResult = true;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}