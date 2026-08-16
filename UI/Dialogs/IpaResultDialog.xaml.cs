// IpaResultDialog.xaml.cs
// Code-behind for the Smart IPA result dialog.
// Shows IPA lookup results and lets the user select one to insert.

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NarraVoice.Core.IPA;

namespace NarraVoice.UI.Dialogs
{
    /// <summary>
    /// View model for a single IPA option in the multiple-results list.
    /// </summary>
    public sealed class IpaOptionViewModel
    {
        public string Ipa { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool HasDescription => !string.IsNullOrEmpty(Description);
    }

    /// <summary>
    /// Dialog showing Smart IPA lookup results.
    /// For single results — shows the IPA with an Insert button.
    /// For homographs — shows all options as clickable buttons.
    /// </summary>
    public partial class IpaResultDialog : Window
    {
        // ── Properties ────────────────────────────────────────────────────────

        /// <summary>The IPA string selected by the user. Empty if cancelled.</summary>
        public string SelectedIpa { get; private set; } = string.Empty;

        /// <summary>True if the user selected an IPA to insert.</summary>
        public bool WasAccepted { get; private set; }

        // ── Constructor ───────────────────────────────────────────────────────

        public IpaResultDialog(string word, List<IpaEntry> results, Window owner)
        {
            InitializeComponent();
            Owner = owner;

            HeaderText.Inlines.Clear();
            HeaderText.Inlines.Add("IPA for: ");
            HeaderText.Inlines.Add(new System.Windows.Documents.Bold(
                new System.Windows.Documents.Run(word)));

            if (results.Count == 1)
            {
                // Single result — show directly
                var entry = results[0];
                SingleIpaText.Text = entry.Ipa;
                SingleDescText.Text = entry.Description;
                SingleDescText.Visibility = string.IsNullOrEmpty(entry.Description)
                    ? Visibility.Collapsed : Visibility.Visible;

                SelectedIpa = entry.Ipa;
                SingleResultPanel.Visibility = Visibility.Visible;
            }
            else
            {
                // Multiple results — show options
                var options = results.Select(r => new IpaOptionViewModel
                {
                    Ipa = r.Ipa,
                    Description = r.Description,
                }).ToList();

                IpaOptionsList.ItemsSource = options;
                MultiResultPanel.Visibility = Visibility.Visible;
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnInsertClick(object sender, RoutedEventArgs e)
        {
            WasAccepted = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            WasAccepted = false;
            SelectedIpa = string.Empty;
            DialogResult = false;
            Close();
        }

        private void OnOptionClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is string ipa)
            {
                SelectedIpa = ipa;
                WasAccepted = true;
                DialogResult = true;
                Close();
            }
        }
    }
}