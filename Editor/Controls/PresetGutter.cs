// PresetGutter.cs
// AvalonEdit margin for NarraVoice.
// Displays preset change markers in a margin to the left of the
// AvalonEdit text area.
//
// Each marker shows:
//   - A rounded colored rectangle (preset color)
//   - The preset name in white text
//
// Clicking a line arms that line with the currently selected preset.
// Right-clicking a marker shows a context menu to remove it or clear all.
// Ctrl+Z undoes the last marker operation.
//
// As an AbstractMargin, this control automatically participates in the
// editor's scroll and visual line layout — no manual scroll-sync code
// is needed.
//
// Each marker is anchored to its line of text via AvalonEdit's TextAnchor.
// This means markers automatically follow their text when lines are
// inserted or deleted above them, and are automatically removed if the
// line they were anchored to is deleted entirely.
//
// The gutter supports a Scale property (adjusted via Ctrl+Alt+/Ctrl+Alt+-)
// which scales the gutter width, marker height, marker radius, padding,
// and font size all together.
 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace NarraVoice.Editor.Controls
{
    /// <summary>
    /// A single preset marker in the gutter margin.
    /// The marker's line number is tracked via a TextAnchor so it
    /// automatically follows its line of text as the document is edited.
    /// </summary>
    public sealed class GutterMarker
    {
        /// <summary>
        /// Anchor to the marker's line of text. When set, <see cref="Line"/>
        /// is derived from this anchor's current position. If the anchored
        /// line is deleted, <see cref="IsDeleted"/> becomes true.
        /// </summary>
        public TextAnchor? Anchor { get; set; }

        /// <summary>
        /// 1-based line number. If <see cref="Anchor"/> is set, this reflects
        /// the anchor's current line. If no anchor is set (e.g. before the
        /// marker has been attached to a document), this is the stored value.
        /// </summary>
        public int Line
        {
            get => Anchor != null && !Anchor.IsDeleted ? Anchor.Line : _line;
            set => _line = value;
        }
        private int _line;

        /// <summary>
        /// True if this marker's anchored line has been deleted from the
        /// document. Markers in this state should be removed.
        /// </summary>
        public bool IsDeleted => Anchor?.IsDeleted ?? false;

        /// <summary>Preset name e.g. "ChickenMan".</summary>
        public string PresetName { get; set; } = string.Empty;

        /// <summary>Hex color string e.g. "#E27B4A".</summary>
        public string Color { get; set; } = "#808080";
    }

    /// <summary>
    /// Custom AvalonEdit margin that displays preset markers alongside
    /// the editor's text area. Markers show a colored rounded rectangle
    /// with the preset name, and automatically participate in the
    /// editor's scroll and layout via AbstractMargin.
    /// </summary>
    public sealed class PresetGutter : AbstractMargin
    {
        // ── Base constants (at Scale = 1.0) ──────────────────────────────────

        private const double BaseGutterWidth = 130;
        private const double BaseMarkerHeight = 14;
        private const double BaseMarkerRadius = 4;
        private const double BaseMarkerPadLeft = 4;
        private const double BaseMarkerPadRight = 4;
        private const double BaseFontSize = 10;
        private const double BaseMarkerMinWidth = 20;

        // ── Scale ─────────────────────────────────────────────────────────────

        private double _scale = 1.0;

        /// <summary>
        /// Scale factor applied to the gutter width, marker height, radius,
        /// padding, and font size. Clamped between 0.7 and 2.0.
        /// Adjusted via Ctrl+Alt+/Ctrl+Alt+-.
        /// </summary>
        public double Scale
        {
            get => _scale;
            set
            {
                _scale = Math.Clamp(value, 0.7, 2.0);
                InvalidateVisual();
                InvalidateMeasure();
            }
        }

        private double GutterWidth => BaseGutterWidth * _scale;
        private double MarkerHeight => BaseMarkerHeight * _scale;
        private double MarkerRadius => BaseMarkerRadius * _scale;
        private double MarkerPadLeft => BaseMarkerPadLeft * _scale;
        private double MarkerPadRight => BaseMarkerPadRight * _scale;
        private double FontSize => BaseFontSize * _scale;
        private double MarkerMinWidth => BaseMarkerMinWidth * _scale;

        // ── State ─────────────────────────────────────────────────────────────

        private List<GutterMarker> _markers = new();
        private readonly Stack<List<GutterMarker>> _undoStack = new();
        private string _armedPreset = string.Empty;
        private string _armedColor = "#808080";

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Raised when a marker is added, removed, or cleared.</summary>
        public event EventHandler<List<GutterMarker>>? MarkersChanged;

        // ── Constructor ───────────────────────────────────────────────────────

        public PresetGutter()
        {
            Cursor = Cursors.Hand;
            Focusable = true;
        }

        // ── Background brush ──────────────────────────────────────────────────

        private static readonly SolidColorBrush _background =
            new(Color.FromRgb(232, 238, 245));

        // ── TextView attachment (AbstractMargin override) ────────────────────

        /// <summary>
        /// Called by AvalonEdit when this margin is attached to or detached
        /// from a TextView. Used to subscribe to document change events
        /// so we can clean up markers whose anchors have been deleted.
        /// </summary>
        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView != null)
            {
                oldTextView.ScrollOffsetChanged -= OnScrollOrLayoutChanged;
                oldTextView.VisualLinesChanged -= OnScrollOrLayoutChanged;
                if (oldTextView.Document != null)
                    oldTextView.Document.Changed -= OnDocumentChanged;
            }

            base.OnTextViewChanged(oldTextView, newTextView);

            if (newTextView != null)
            {
                newTextView.ScrollOffsetChanged += OnScrollOrLayoutChanged;
                newTextView.VisualLinesChanged += OnScrollOrLayoutChanged;
                if (newTextView.Document != null)
                    newTextView.Document.Changed += OnDocumentChanged;
            }

            InvalidateVisual();
        }

        private void OnScrollOrLayoutChanged(object? sender, EventArgs e)
        {
            InvalidateVisual();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Set the currently armed preset — the one that will be applied
        /// when the user clicks a line in the gutter.
        /// </summary>
        public void ArmPreset(string presetName, string color)
        {
            _armedPreset = presetName;
            _armedColor = color;
        }

        /// <summary>
        /// Set all markers at once (e.g. when loading a chunk).
        /// Each marker's stored line number is converted into a TextAnchor
        /// at that line's start offset, so it will track the line going
        /// forward. Does not push to undo stack.
        /// </summary>
        public void SetMarkers(List<GutterMarker> markers)
        {
            _markers = markers.ToList();

            if (TextView?.Document != null)
            {
                foreach (var marker in _markers)
                    AttachAnchor(marker, marker.Line);
            }

            _undoStack.Clear();
            InvalidateVisual();
        }

        /// <summary>
        /// Current markers — call after MarkersChanged event.
        /// Returns markers with their up-to-date Line values resolved
        /// from their anchors.
        /// </summary>
        public List<GutterMarker> GetMarkers() => _markers.ToList();

        /// <summary>Remove all markers for the current chunk.</summary>
        public void ClearAllMarkers()
        {
            if (_markers.Count == 0) return;
            PushUndo();
            _markers.Clear();
            InvalidateVisual();
            MarkersChanged?.Invoke(this, GetMarkers());
        }

        /// <summary>Undo the last marker operation.</summary>
        public void UndoLastMarker()
        {
            if (_undoStack.Count == 0) return;
            _markers = _undoStack.Pop();
            InvalidateVisual();
            MarkersChanged?.Invoke(this, GetMarkers());
        }

        // ── Document change handling ─────────────────────────────────────────

        /// <summary>
        /// Called whenever the document text changes. Removes any markers
        /// whose anchored line has been deleted, and redraws to reflect
        /// markers that may have shifted to new line numbers.
        /// </summary>
        private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
        {
            int before = _markers.Count;
            _markers.RemoveAll(m => m.IsDeleted);

            InvalidateVisual();

            if (_markers.Count != before)
                MarkersChanged?.Invoke(this, GetMarkers());
        }

        /// <summary>
        /// Create and attach a TextAnchor for a marker at the start of
        /// the given 1-based line number.
        /// </summary>
        private void AttachAnchor(GutterMarker marker, int lineNumber)
        {
            var document = TextView?.Document;
            if (document == null) return;

            int clampedLine = Math.Clamp(lineNumber, 1, document.LineCount);
            var line = document.GetLineByNumber(clampedLine);

            var anchor = document.CreateAnchor(line.Offset);
            anchor.MovementType = AnchorMovementType.Default;
            anchor.SurviveDeletion = false;

            marker.Anchor = anchor;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            var textView = TextView;
            double height = textView?.RenderSize.Height ?? RenderSize.Height;

            // Draw background
            dc.DrawRectangle(_background, null,
                new Rect(0, 0, GutterWidth, height));

            if (textView == null || !textView.VisualLinesValid) return;

            var typeface = new Typeface("Segoe UI");

            foreach (var marker in _markers)
            {
                if (marker.IsDeleted) continue;

                double? y = GetYForLine(marker.Line);
                if (y == null) continue;

                double top = y.Value + (textView.DefaultLineHeight - MarkerHeight) / 2;
                double width = GutterWidth - MarkerPadLeft - MarkerPadRight;

                // Parse color
                SolidColorBrush brush;
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(
                        marker.Color.StartsWith('#') ? marker.Color : "#" + marker.Color);
                    brush = new SolidColorBrush(color);
                }
                catch { brush = new SolidColorBrush(Colors.Gray); }

                // Draw rounded rectangle
                var rect = new Rect(MarkerPadLeft, top, width, MarkerHeight);
                dc.DrawRoundedRectangle(brush, null, rect, MarkerRadius, MarkerRadius);

                // Draw preset name in white
                string label = marker.PresetName.Length > 14
                    ? marker.PresetName[..14]
                    : marker.PresetName;

                var ft = new FormattedText(
                    label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    FontSize,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                double textX = MarkerPadLeft + 4 * _scale;
                double textY = top + (MarkerHeight - ft.Height) / 2;
                dc.DrawText(ft, new Point(textX, textY));
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double height = double.IsInfinity(availableSize.Height)
                ? 0
                : availableSize.Height;
            return new Size(GutterWidth, height);
        }

        // ── Mouse handling ────────────────────────────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (TextView?.Document == null || string.IsNullOrEmpty(_armedPreset))
                return;

            int? line = GetLineAtY(e.GetPosition(this).Y);
            if (line == null)
                return;

            PushUndo();
            _markers.RemoveAll(m => m.Line == line.Value);

            var marker = new GutterMarker
            {
                PresetName = _armedPreset,
                Color = _armedColor,
            };
            AttachAnchor(marker, line.Value);
            _markers.Add(marker);

            _markers.Sort((a, b) => a.Line.CompareTo(b.Line));
            InvalidateVisual();
            MarkersChanged?.Invoke(this, GetMarkers());
            e.Handled = true;
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (TextView?.Document == null) return;

            int? line = GetLineAtY(e.GetPosition(this).Y);
            var marker = line.HasValue
                ? _markers.FirstOrDefault(m => m.Line == line.Value)
                : null;

            var menu = new ContextMenu();

            if (marker != null)
            {
                var removeItem = new MenuItem
                {
                    Header = $"Remove Marker (line {marker.Line})"
                };
                removeItem.Click += (s, ev) => RemoveMarker(marker);
                menu.Items.Add(removeItem);
                menu.Items.Add(new Separator());
            }

            var clearItem = new MenuItem { Header = "Clear All Markers" };
            clearItem.IsEnabled = _markers.Count > 0;
            clearItem.Click += (s, ev) => ClearAllMarkers();
            menu.Items.Add(clearItem);

            var undoItem = new MenuItem { Header = "Undo Last Marker (Ctrl+Z)" };
            undoItem.IsEnabled = _undoStack.Count > 0;
            undoItem.Click += (s, ev) => UndoLastMarker();
            menu.Items.Add(undoItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Z &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                UndoLastMarker();
                e.Handled = true;
            }
        }

        // ── Line position helpers ─────────────────────────────────────────────

        /// <summary>
        /// Get the Y position of a line in the gutter, accounting for scroll.
        /// Returns null if the line is not currently visible.
        /// </summary>
        private double? GetYForLine(int lineNumber)
        {
            var textView = TextView;
            if (textView == null || !textView.VisualLinesValid) return null;

            foreach (var vl in textView.VisualLines)
            {
                if (vl.FirstDocumentLine.LineNumber == lineNumber)
                {
                    double y = vl.VisualTop - textView.ScrollOffset.Y;
                    return y;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the document line number at a Y position in the gutter.
        /// </summary>
        private int? GetLineAtY(double y)
        {
            var textView = TextView;
            if (textView == null || !textView.VisualLinesValid) return null;

            foreach (var vl in textView.VisualLines)
            {
                double top = vl.VisualTop - textView.ScrollOffset.Y;
                double bottom = top + vl.Height;
                if (y >= top && y <= bottom)
                    return vl.FirstDocumentLine.LineNumber;
            }
            return null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void RemoveMarker(GutterMarker marker)
        {
            PushUndo();
            _markers.Remove(marker);
            InvalidateVisual();
            MarkersChanged?.Invoke(this, GetMarkers());
        }

        private void PushUndo()
        {
            _undoStack.Push(_markers.ToList());
            // Keep undo stack from growing too large
            if (_undoStack.Count > 20)
            {
                var temp = _undoStack.ToList();
                temp.RemoveAt(temp.Count - 1);
                _undoStack.Clear();
                foreach (var item in temp)
                    _undoStack.Push(item);
            }
        }
    }
}