using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using Ookii.Dialogs.Wpf;

namespace NarraVoice.UI.Dialogs
{
    public class MergeFileItem : INotifyPropertyChanged
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FullPath);

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MergeFilesDialog : Window
    {
        public ObservableCollection<MergeFileItem> Files { get; } = new();
        public string? SelectedFolder { get; private set; }
        public bool Confirmed { get; private set; }

        public List<string> OrderedSelectedFiles =>
            Files.Where(f => f.IsSelected).Select(f => f.FullPath).ToList();

        public MergeFilesDialog()
        {
            InitializeComponent();
            FileList.ItemsSource = Files;
        }

        private void OnChooseFolder(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog
            {
                Description = "Select a folder containing audio files to merge",
                UseDescriptionForTitle = true,
            };

            if (dlg.ShowDialog(this) != true) return;

            string folder = dlg.SelectedPath;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            SelectedFolder = folder;
            FolderLabel.Text = folder;

            var foundFiles = Directory.GetFiles(folder, "*.wav")
                .Concat(Directory.GetFiles(folder, "*.mp3"))
                .OrderBy(f => f)
                .ToList();

            Files.Clear();
            foreach (var f in foundFiles)
                Files.Add(new MergeFileItem { FullPath = f, IsSelected = false });

            if (Files.Count == 0)
            {
                MessageBox.Show("No .wav or .mp3 files found in that folder.",
                    "Merge Files", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void OnMoveUp(object sender, RoutedEventArgs e)
        {
            int i = FileList.SelectedIndex;
            if (i <= 0) return;
            (Files[i - 1], Files[i]) = (Files[i], Files[i - 1]);
            FileList.SelectedIndex = i - 1;
        }

        private void OnMoveDown(object sender, RoutedEventArgs e)
        {
            int i = FileList.SelectedIndex;
            if (i < 0 || i >= Files.Count - 1) return;
            (Files[i + 1], Files[i]) = (Files[i], Files[i + 1]);
            FileList.SelectedIndex = i + 1;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
        }

        private void OnMerge(object sender, RoutedEventArgs e)
        {
            if (OrderedSelectedFiles.Count == 0)
            {
                MessageBox.Show("Check at least one file to merge.",
                    "Merge Files", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Confirmed = true;
            DialogResult = true;
        }
    }
}