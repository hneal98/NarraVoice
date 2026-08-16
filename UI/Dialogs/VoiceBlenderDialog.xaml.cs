// VoiceBlenderDialog.xaml.cs
// Dialog for blending up to three Kokoro voices into a new custom voice.
// Allows previewing the blend before saving as a .npy voice file.

using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Microsoft.ML.OnnxRuntime;
using NarraVoice.Core.Config;
using NarraVoice.Core.Engine;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NarraVoice.UI.Dialogs
{
    public partial class VoiceBlenderDialog : Window
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly KokoroTTS _tts;
        private readonly List<(string Id, string Label)> _voices;
        private bool _isUpdatingSliders = false;

        // ── Constructor ───────────────────────────────────────────────────────

        public VoiceBlenderDialog(
            KokoroTTS tts,
            List<(string Id, string Label)> voices,
            Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _tts = tts;
            _voices = voices;

            PopulateVoiceCombos();
        }

        // ── Voice dropdowns ───────────────────────────────────────────────────

        private void PopulateVoiceCombos()
        {
            var kokoroOnlyVoices = _voices.Where(v => !v.Id.StartsWith("qwen_", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var combo in new[] { Voice1Combo, Voice2Combo, Voice3Combo })
            {
                combo.Items.Clear();
                foreach (var (id, label) in kokoroOnlyVoices)
                {
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = label,
                        Tag = id,
                    });
                }
                combo.SelectedIndex = 0;
            }
        }

        // ── Slider logic ──────────────────────────────────────────────────────

        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders) return;
            if (Voice1Slider == null || Voice2Slider == null || Voice3Slider == null) return;

            _isUpdatingSliders = true;

            try
            {
                var changed = sender as Slider;

                double v1 = Voice1Slider.Value;
                double v2 = Voice2Slider.Value;
                double v3 = Voice3Slider.Value;
                double total = v1 + v2 + v3;

                if (total > 100)
                {
                    double excess = total - 100;
                    if (changed == Voice1Slider)
                        Voice1Slider.Value = Math.Max(0, v1 - excess);
                    else if (changed == Voice2Slider)
                        Voice2Slider.Value = Math.Max(0, v2 - excess);
                    else
                        Voice3Slider.Value = Math.Max(0, v3 - excess);
                }

                // Always update labels
                Voice1Label.Text = $"{(int)Voice1Slider.Value}%";
                Voice2Label.Text = $"{(int)Voice2Slider.Value}%";
                Voice3Label.Text = $"{(int)Voice3Slider.Value}%";

                int newTotal = (int)Voice1Slider.Value + (int)Voice2Slider.Value + (int)Voice3Slider.Value;
                TotalLabel.Text = $"{newTotal}%";
                TotalWarning.Visibility = newTotal != 100
                    ? Visibility.Visible : Visibility.Collapsed;
                SaveBtn.IsEnabled = newTotal == 100 && !string.IsNullOrWhiteSpace(VoiceNameBox.Text);
            }
            finally
            {
                _isUpdatingSliders = false;
            }
        }

        // ── Preview ───────────────────────────────────────────────────────────

        private void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var blended = BuildBlendedVoice();
                if (blended == null) return;

                string testText = TestSentenceBox.Text.Trim();
                if (string.IsNullOrEmpty(testText)) return;

                var config = new KokoroTTSPipelineConfig();
                _tts.SpeakFast(testText, blended, config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Preview error: {ex.Message}",
                    "Voice Blender", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string name = VoiceNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a name for the new voice.",
                    "Voice Blender", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var blended = BuildBlendedVoice();
                if (blended == null) return;

                // Save to voices folder
                string voicesDir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "voices");
                Directory.CreateDirectory(voicesDir);

                // Name format matches Kokoro convention e.g. "am_fenrir"
                // For blended voices use "xx_" prefix
                string fileName = $"xx_{name}.npy";
                string filePath = Path.Combine(voicesDir, fileName);

                if (File.Exists(filePath))
                {
                    var result = MessageBox.Show(
                        $"A voice named '{name}' already exists. Overwrite?",
                        "Voice Blender", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes) return;
                }

                blended.Rename(name, KokoroLanguage.AmericanEnglish, KokoroGender.Female);
                blended.Export(filePath);

                MessageBox.Show($"Voice '{name}' saved successfully.",
                    "Voice Blender", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save error: {ex.Message}",
                    "Voice Blender", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Cancel ────────────────────────────────────────────────────────────

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private KokoroVoice? BuildBlendedVoice()
        {
            var voicesToMix = new List<(KokoroVoice voice, float weight)>();

            string id1 = (Voice1Combo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
            string id2 = (Voice2Combo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
            string id3 = (Voice3Combo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

            float w1 = (float)Voice1Slider.Value / 100f;
            float w2 = (float)Voice2Slider.Value / 100f;
            float w3 = (float)Voice3Slider.Value / 100f;

            if (w1 > 0 && !string.IsNullOrEmpty(id1))
                voicesToMix.Add((KokoroVoiceManager.GetVoice(id1), w1));
            if (w2 > 0 && !string.IsNullOrEmpty(id2))
                voicesToMix.Add((KokoroVoiceManager.GetVoice(id2), w2));
            if (w3 > 0 && !string.IsNullOrEmpty(id3))
                voicesToMix.Add((KokoroVoiceManager.GetVoice(id3), w3));

            if (voicesToMix.Count == 0)
            {
                MessageBox.Show("At least one voice must have a weight greater than 0.",
                    "Voice Blender", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            if (voicesToMix.Count == 1)
                return voicesToMix[0].voice;

            return KokoroVoiceManager.Mix(voicesToMix.ToArray());
        }

        private void OnVoiceNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (SaveBtn != null)
            {
                int total = (int)Voice1Slider.Value + (int)Voice2Slider.Value + (int)Voice3Slider.Value;
                SaveBtn.IsEnabled = total == 100 && !string.IsNullOrWhiteSpace(VoiceNameBox.Text);
            }
        }
    }
}