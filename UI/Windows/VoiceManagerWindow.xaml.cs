// VoiceManagerWindow.xaml.cs
// Voice Manager — lets users choose which voices appear in the dropdown.
// Also handles first-run download of all voice files.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NarraVoice.Core.Services;


namespace NarraVoice.UI.Windows
{
    public partial class VoiceManagerWindow : Window
    {
        public event EventHandler? PreferencesChanged;

        private readonly VoiceManagerService _service;
        private readonly Dictionary<string, CheckBox> _checkboxes = new();
        private CancellationTokenSource? _cts;

        public VoiceManagerWindow(VoiceManagerService service, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _service = service;

            BuildVoiceList();
            CheckDownloadStatus();
        }

        // ── Voice list ────────────────────────────────────────────────────────

        private void BuildVoiceList()
        {
            VoiceListPanel.Children.Clear();
            _checkboxes.Clear();

            var voices = _service.GetAllVoices();
            var groups = voices.GroupBy(v => v.Category).OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                // Group header
                var header = new TextBlock
                {
                    Text = group.Key,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 8, 0, 4),
                };
                VoiceListPanel.Children.Add(header);

                foreach (var voice in group)
                {
                    var panel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, 2, 0, 2),
                    };

                    var cb = new CheckBox
                    {
                        Content = voice.Label,
                        IsChecked = voice.IsVisible,
                        IsEnabled = voice.IsInstalled,
                        Width = 160,
                        VerticalContentAlignment = VerticalAlignment.Center,
                    };

                    // Show install status
                    var status = new TextBlock
                    {
                        Text = voice.IsInstalled ? "✓ installed" : "not downloaded",
                        FontSize = 10,
                        Foreground = voice.IsInstalled
                            ? new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60))
                            : Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                    };

                    panel.Children.Add(cb);
                    panel.Children.Add(status);
                    VoiceListPanel.Children.Add(panel);
                    _checkboxes[voice.Id] = cb;
                }
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int total = _checkboxes.Count;
            int checked_ = _checkboxes.Values.Count(cb => cb.IsChecked == true);
            int installed = _service.GetInstalledVoiceIds().Count;
            StatusLabel.Text =
                $"{checked_} of {total} voices visible in dropdown  |  " +
                $"{installed} of {total} voices installed";
        }

        // ── Download status ───────────────────────────────────────────────────

        private void CheckDownloadStatus()
        {
            if (!_service.AllVoicesInstalled)
            {
                DownloadBanner.Visibility = Visibility.Visible;
                DownloadStatusText.Text = "Some voices not yet downloaded. Starting download...";
                _ = DownloadMissingVoicesAsync();
            }
        }

        private async System.Threading.Tasks.Task DownloadMissingVoicesAsync()
        {
            _cts = new CancellationTokenSource();

            var progress = new Progress<VoiceDownloadProgress>(p =>
            {
                DownloadProgress.Maximum = p.Total;
                DownloadProgress.Value = p.Current;
                DownloadDetailText.Text = p.Success
                    ? $"Downloaded {p.VoiceName} ({p.Current}/{p.Total})"
                    : $"Downloading {p.VoiceName}... ({p.Current}/{p.Total})";
            });

            try
            {
                int count = await _service.DownloadAllVoicesAsync(
                    progress, _cts.Token);

                DownloadStatusText.Text = $"Download complete — {count} voices downloaded.";
                DownloadDetailText.Text = string.Empty;
                DownloadProgress.Value = DownloadProgress.Maximum;

                // Refresh the list to show newly installed voices
                BuildVoiceList();
            }
            catch (OperationCanceledException)
            {
                DownloadStatusText.Text = "Download cancelled.";
            }
            catch (Exception ex)
            {
                DownloadStatusText.Text = $"Download error: {ex.Message}";
            }
        }

        // ── Selection helpers ─────────────────────────────────────────────────

        private void OnShowAll(object sender, RoutedEventArgs e)
        {
            foreach (var cb in _checkboxes.Values.Where(cb => cb.IsEnabled))
                cb.IsChecked = true;
            UpdateStatus();
        }

        private void OnHideAll(object sender, RoutedEventArgs e)
        {
            foreach (var cb in _checkboxes.Values)
                cb.IsChecked = false;
            UpdateStatus();
        }

        private void OnEnglishOnly(object sender, RoutedEventArgs e)
        {
            var englishIds = VoiceManagerService.AllVoices
                .Where(v => v.Language == "en-us" || v.Language == "en-gb")
                .Select(v => v.Id)
                .ToHashSet();

            foreach (var (id, cb) in _checkboxes)
                cb.IsChecked = cb.IsEnabled && englishIds.Contains(id);

            UpdateStatus();
        }

        // ── Save/Cancel ───────────────────────────────────────────────────────

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var visible = _checkboxes
                .Where(kvp => kvp.Value.IsChecked == true)
                .Select(kvp => kvp.Key)
                .ToList();

            _service.SavePreferences(visible);
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
            //DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            //DialogResult = false;
            Close();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _cts?.Cancel();
        }
    }
}