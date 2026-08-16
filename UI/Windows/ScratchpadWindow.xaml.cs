// ScratchpadWindow.xaml.cs
// Code-behind for the Scratchpad voice tester window.
// Non-modal window for testing voices and pronunciations
// without affecting the story. Opens with the main window's
// current voice/rate/pitch settings. Nothing is saved on close.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NarraVoice.Core.Engine;
using NarraVoice.Core.IPA;
using NarraVoice.Core.Models;
using NarraVoice.Core.Services;
using NarraVoice.UI.Dialogs;

namespace NarraVoice.UI.Windows
{
    public partial class ScratchpadWindow : Window
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly RenderPipeline _pipeline;
        private readonly IpaLookupService _ipaService;
        private readonly AudioPlayerService _player;

        // ── State ─────────────────────────────────────────────────────────────

        private CancellationTokenSource? _cts;
        private bool _isRendering;

        // ── Constructor ───────────────────────────────────────────────────────

        public ScratchpadWindow(
            RenderPipeline pipeline,
            IpaLookupService ipaService,
            List<(string Id, string Label)> voices,
            string currentVoiceId,
            string currentRate,
            string currentPitch,
            string currentVolume = "100%")
        {
            InitializeComponent();

            _pipeline = pipeline;
            _ipaService = ipaService;
            _player = new AudioPlayerService();

            // Populate voice dropdown
            foreach (var (id, label) in voices)
                VoiceCombo.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = id,
                });

            // Select current voice
            for (int i = 0; i < VoiceCombo.Items.Count; i++)
            {
                if (VoiceCombo.Items[i] is ComboBoxItem item &&
                    item.Tag as string == currentVoiceId)
                {
                    VoiceCombo.SelectedIndex = i;
                    break;
                }
            }

            // Set rate and pitch sliders
            if (int.TryParse(currentRate.Replace("%", ""), out int rate))
                RateSlider.Value = rate;
            if (int.TryParse(currentPitch.Replace("st", ""), out int pitch))
                PitchSlider.Value = pitch;
            if (int.TryParse(currentVolume.Replace("%", ""), out int volume))
                VolumeSlider.Value = volume;
        }

        // ── Slider events ─────────────────────────────────────────────────────

        private void OnRateChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RateLabel != null)
                RateLabel.Text = $"{(int)e.NewValue:+0;-0;+0}%";
        }

        private void OnPitchChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchLabel != null)
                PitchLabel.Text = $"{e.NewValue:+0.##;-0.##;+0}st";
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeLabel != null)
                VolumeLabel.Text = $"{(int)e.NewValue}%";
        }

        // ── Context menu (Smart IPA) ──────────────────────────────────────────

        private void OnSmartIpaRequested(object? sender, string wordAndOffset)
        {
            var parts = wordAndOffset.Split('|');
            string word = parts[0];
            int offset = parts.Length > 1 && int.TryParse(parts[1], out int o) ? o : -1;

            var results = _ipaService.Lookup(word);
            if (results.Count == 0)
            {
                MessageBox.Show($"No IPA found for \"{word}\".",
                    "Smart IPA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new IpaResultDialog(word, results, this);
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedIpa))
            {
                string insert = IpaLookupService.FormatForInsert(word, dlg.SelectedIpa);
                if (offset >= 0)
                    TextEditor.ReplaceWordAtOffset(offset, insert);
                else
                    TextEditor.InsertAtCaret(insert);
            }
        }


        private void ShowIpaPopup(string word)
        {
            var results = _ipaService.Lookup(word);
            if (results.Count == 0)
            {
                MessageBox.Show($"No IPA found for \"{word}\".",
                    "Smart IPA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new IpaResultDialog(word, results, this);
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedIpa))
            {
                string insert = IpaLookupService.FormatForInsert(word, dlg.SelectedIpa);
                TextEditor.InsertAtCaret(insert);
            }
        }

        // ── Preview ───────────────────────────────────────────────────────────

        private async void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            string text = TextEditor.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (_isRendering) return;

            _isRendering = true;
            PreviewBtn.IsEnabled = false;
            PreviewBtn.Content = "Rendering...";
            LogView.Clear();

            _cts = new CancellationTokenSource();

            try
            {
                // Build voice profile from current settings
                string voiceId = "+0%";
                if (VoiceCombo.SelectedItem is ComboBoxItem item)
                    voiceId = item.Tag as string ?? "af_heart";

                var profile = new VoiceProfile(
                    voiceId,
                    RateLabel.Text,
                    PitchLabel.Text,
                    VolumeLabel.Text);

                // Render to temp folder
                string tmpDir = Path.Combine(
                    Path.GetTempPath(), "NarraVoice_Scratch");

                var result = await _pipeline.RenderChunkAsync(
                    text, profile, tmpDir,
                    chunkIndex: -1,
                    prefix: "scratch",
                    log: msg => Dispatcher.Invoke(() =>
                    {
                        LogView.AppendText(msg + "\n");
                        LogView.ScrollToEnd();
                    }),
                    cancellationToken: _cts.Token);
            }

            //if (result.Success)

            //        _player.Load(result.Mp3Path, autoplay: true);

            catch (OperationCanceledException)
            {
                AppendLog("Cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                _isRendering = false;
                PreviewBtn.IsEnabled = true;
                PreviewBtn.Content = "🔍 Preview";
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _player.Stop();
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            TextEditor.Clear();
            LogView.Clear();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _cts?.Cancel();
            _player.Dispose();
        }

        private void AppendLog(string msg) =>
            Dispatcher.Invoke(() =>
            {
                LogView.AppendText(msg + "\n");
                LogView.ScrollToEnd();
            });
    }
}