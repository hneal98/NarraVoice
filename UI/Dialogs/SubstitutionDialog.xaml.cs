// SubstitutionDialog.xaml.cs
// Code-behind for the Pronunciation Substitutions dialog.
// Edits the global substitutions list via SubstitutionService.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using NarraVoice.Core.Services;

namespace NarraVoice.UI.Dialogs
{
    /// <summary>
    /// View model for a single substitution row in the DataGrid.
    /// </summary>
    public sealed class SubstitutionRow : INotifyPropertyChanged
    {
        private string _original = string.Empty;
        private string _replacement = string.Empty;

        public string Original
        {
            get => _original;
            set { _original = value; OnPropertyChanged(nameof(Original)); }
        }

        public string Replacement
        {
            get => _replacement;
            set { _replacement = value; OnPropertyChanged(nameof(Replacement)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Dialog for editing global pronunciation substitutions.
    /// </summary>
    public partial class SubstitutionDialog : Window
    {
        private readonly SubstitutionService _service;
        private readonly ObservableCollection<SubstitutionRow> _rows = new();

        public SubstitutionDialog(SubstitutionService service, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _service = service;

            // Load existing substitutions into the grid
            foreach (var kvp in _service.Substitutions)
            {
                _rows.Add(new SubstitutionRow
                {
                    Original = kvp.Key,
                    Replacement = kvp.Value,
                });
            }

            SubstitutionGrid.ItemsSource = _rows;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            string original = NewOriginalBox.Text.Trim();
            string replacement = NewReplacementBox.Text.Trim();

            if (string.IsNullOrEmpty(original)) return;

            // Check for duplicate
            foreach (var row in _rows)
            {
                if (row.Original == original)
                {
                    row.Replacement = replacement;
                    NewOriginalBox.Clear();
                    NewReplacementBox.Clear();
                    return;
                }
            }

            _rows.Add(new SubstitutionRow
            {
                Original = original,
                Replacement = replacement,
            });

            NewOriginalBox.Clear();
            NewReplacementBox.Clear();
            NewOriginalBox.Focus();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Commit any pending edits in the DataGrid
            SubstitutionGrid.CommitEdit(
                System.Windows.Controls.DataGridEditingUnit.Row, true);

            // Build new substitutions dict from grid rows
            var newSubs = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var row in _rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Original))
                    newSubs[row.Original] = row.Replacement;
            }

            _service.SetAll(newSubs);
            _service.Save();

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
