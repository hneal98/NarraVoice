// SmartTextEditor.cs
// AvalonEdit-based text editor for NarraVoice chunk editing.
// Wraps ICSharpCode.AvalonEdit.TextEditor with:
//   - Syntax highlighting for <sil:Nms> tags and [word](/ipa/) overrides
//   - Right-click context menu with Smart IPA lookup
//   - Word-under-cursor detection for IPA lookup
//   - Edit tracking (unsaved changes detection)
//   - Line count and position reporting

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace NarraVoice.Editor.Controls
{
    /// <summary>
    /// Extended AvalonEdit TextEditor for NarraVoice chunk editing.
    /// Adds Smart IPA context menu, tag highlighting, and edit tracking.
    /// </summary>
    public sealed class SmartTextEditor : TextEditor
    {
        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when the user right-clicks a word and selects Smart IPA.
        /// The string argument is the word that was right-clicked.
        /// </summary>
        public event EventHandler<string>? SmartIpaRequested;

        /// <summary>Raised when the document is modified.</summary>
        public event EventHandler? DocumentModified;

        // ── State ─────────────────────────────────────────────────────────────

        private bool _hasUnsavedChanges;
        private string _lastSavedText = string.Empty;

        // ── Constructor ───────────────────────────────────────────────────────

        public SmartTextEditor()
        {
            // Basic editor settings
            FontFamily = new FontFamily("Consolas, Segoe UI, Arial");
            FontSize = 13;
            WordWrap = true;
            ShowLineNumbers = false;
            IsReadOnly = false;
            Padding = new Thickness(4);

            // Enable context menu
            ContextMenuOpening += OnContextMenuOpening;

            // Track changes
            Document.Changed += OnDocumentChanged;

            // Apply NarraVoice syntax highlighting
            ApplyHighlighting();

            // Zoom in/out with Ctrl+/Ctrl-
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ── Properties ────────────────────────────────────────────────────────

        /// <summary>True if the document has unsaved changes.</summary>
        public bool HasUnsavedChanges => _hasUnsavedChanges;

        /// <summary>Total number of lines in the document.</summary>
        public new int LineCount => Document.LineCount;

        /// <summary>Current cursor line number (1-based).</summary>
        public int CurrentLine =>
            Document.GetLineByOffset(CaretOffset).LineNumber;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Load text into the editor and mark as saved.
        /// </summary>
        public void LoadText(string text)
        {
            Document.Text = text ?? string.Empty;
            _lastSavedText = Document.Text;
            _hasUnsavedChanges = false;
        }


        /// <summary>
        /// Mark the current state as saved.
        /// </summary>
        public void MarkSaved()
        {
            _lastSavedText = Document.Text;
            _hasUnsavedChanges = false;
        }

        /// <summary>
        /// Get the word under the cursor at the given position.
        /// Returns empty string if no word is found.
        /// </summary>
        public string GetWordAtPosition(Point position)
        {
            int offset = GetOffsetFromMousePosition(position);
            if (offset < 0) return string.Empty;
            return GetWordAtOffset(offset);
        }

        /// <summary>
        /// Insert text at the current caret position,
        /// replacing any selected text.
        /// </summary>
        public void InsertAtCaret(string text)
        {
            Document.Replace(SelectionStart, SelectionLength, text);
        }

        /// <summary>
        /// Replace the word at the given offset with new text.
        /// </summary>
        public void ReplaceWordAtOffset(int offset, string newText)
        {
            var segment = GetWordSegmentAtOffset(offset);
            if (segment != null)
                Document.Replace(segment.Offset, segment.Length, newText);
        }

        /// <summary>
        /// Get the 1-based start and end line numbers of the current selection.
        /// If no selection, returns the current line for both.
        /// </summary>
        public (int startLine, int endLine) GetSelectionLineRange()
        {
            int startOffset = SelectionStart;
            int endOffset = SelectionStart + SelectionLength;
            int startLine = Document.GetLineByOffset(startOffset).LineNumber;
            int endLine = Document.GetLineByOffset(endOffset).LineNumber;
            return (startLine, endLine);
        }

        public IReadOnlyDictionary<string, string>? Substitutions { get; set; }

        // ── Context menu ──────────────────────────────────────────────────────

        private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = new System.Windows.Controls.ContextMenu();

            // Standard edit actions
            AddMenuItem(menu, "Cut", ApplicationCommands.Cut);
            AddMenuItem(menu, "Copy", ApplicationCommands.Copy);
            AddMenuItem(menu, "Paste", ApplicationCommands.Paste);
            menu.Items.Add(new System.Windows.Controls.Separator());

            // Smart IPA
            var mousePos = Mouse.GetPosition(this);
            int offset = GetOffsetFromMousePosition(mousePos);
            string word = offset >= 0 ? GetWordAtOffset(offset) : string.Empty;

            if (!string.IsNullOrEmpty(word) && IsAlpha(word))
            {
                bool alreadySubstituted = Substitutions != null &&
                    (Substitutions.ContainsKey(word) || Substitutions.ContainsKey(word.ToLower()));

                var ipaItem = new System.Windows.Controls.MenuItem
                {
                    Header = alreadySubstituted
                        ? $"Smart IPA: \"{word}\" ✓"
                        : $"Smart IPA: \"{word}\""
                };
                string capturedWord = word;
                int capturedOffset = offset;
                ipaItem.Click += (s, ev) =>
                {
                    SmartIpaRequested?.Invoke(this, $"{capturedWord}|{capturedOffset}");
                };
                menu.Items.Add(ipaItem);
            }
            else
            {
                var ipaItem = new System.Windows.Controls.MenuItem
                {
                    Header = "Smart IPA — click a word",
                    IsEnabled = false,
                };
                menu.Items.Add(ipaItem);
            }

            ContextMenu = menu;
        }

        private static void AddMenuItem(
            System.Windows.Controls.ContextMenu menu,
            string header,
            ICommand command)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = header,
                Command = command,
            });
        }

        // ── Syntax highlighting ───────────────────────────────────────────────

        private void ApplyHighlighting()
        {
            // Define inline XSHD highlighting for NarraVoice tags
            string xshd = @"<?xml version=""1.0""?>
<SyntaxDefinition name=""NarraVoice"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""SilenceTag"" foreground=""#2196F3"" fontWeight=""bold""/>
  <Color name=""IpaOverride"" foreground=""#4CAF50"" fontWeight=""bold""/>
  <RuleSet>
    <Rule color=""SilenceTag"">
      &lt;sil:\d+ms&gt;
    </Rule>
    <Rule color=""IpaOverride"">
      \[.+?\]\(/.+?/\)
    </Rule>
  </RuleSet>
</SyntaxDefinition>";

            try
            {
                using var reader = XmlReader.Create(
                    new System.IO.StringReader(xshd));
                SyntaxHighlighting = HighlightingLoader.Load(
                    reader, HighlightingManager.Instance);
            }
            catch
            {
                // Highlighting is cosmetic — fail silently
            }
        }

        // ── Change tracking ───────────────────────────────────────────────────

        private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
        {
            _hasUnsavedChanges = Document.Text != _lastSavedText;
            DocumentModified?.Invoke(this, EventArgs.Empty);
        }

        // ── Word detection helpers ────────────────────────────────────────────

        private int GetOffsetFromMousePosition(Point position)
        {
            try
            {
                var textView = TextArea.TextView;

                // Account for the gutter margin (get its width from the first left margin)
                double gutterWidth = 0;
                if (TextArea.LeftMargins.Count > 0 && TextArea.LeftMargins[0] is PresetGutter gutter)
                    gutterWidth = gutter.ActualWidth;

                double adjustedX = position.X - gutterWidth;

                var pos = textView.GetPosition(
                    new Point(adjustedX, position.Y) +
                    new Vector(textView.ScrollOffset.X, textView.ScrollOffset.Y));

                if (pos == null) return -1;
                var line = Document.GetLineByNumber(pos.Value.Line);
                int offset = line.Offset + pos.Value.Column - 1;
                return Math.Clamp(offset, 0, Document.TextLength - 1);
            }
            catch { return -1; }
        }

        private string GetWordAtOffset(int offset)
        {
            var segment = GetWordSegmentAtOffset(offset);
            return segment != null
                ? Document.GetText(segment.Offset, segment.Length)
                : string.Empty;
        }

        private ISegment? GetWordSegmentAtOffset(int offset)
        {
            if (offset < 0 || offset >= Document.TextLength)
                return null;

            string text = Document.Text;
            int start = offset;
            int end = offset;

            // Expand left
            while (start > 0 && IsWordChar(text[start - 1]))
                start--;

            // Expand right
            while (end < text.Length && IsWordChar(text[end]))
                end++;

            if (end <= start) return null;

            return new WordSegment(start, end - start);
        }

        // Allow Zoom in and out.
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    FontSize = Math.Min(FontSize + 1, 32);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    FontSize = Math.Max(FontSize - 1, 8);
                    e.Handled = true;
                }
            }
        }

        // Simple segment implementation since SimpleSegment is internal to AvalonEdit
        private sealed class WordSegment : ISegment
        {
            public int Offset { get; }
            public int Length { get; }
            public int EndOffset => Offset + Length;
            public WordSegment(int offset, int length)
            {
                Offset = offset;
                Length = length;
            }
        }

        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '\'' || c == '-';

        private static bool IsAlpha(string word) =>
            !string.IsNullOrEmpty(word) &&
            System.Text.RegularExpressions.Regex.IsMatch(word, @"^[a-zA-Z'-]+$");
    }
}