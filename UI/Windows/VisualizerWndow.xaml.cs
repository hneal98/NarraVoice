// VisualizerWindow.xaml.cs
// 3-panel analysis: Waveform, Pitch, Energy.
// Pages by fixed time windows (default 8s) so long files stay readable.
using NarraVoice.Core.Config;
using NarraVoice.Core.Services;
using NAudio.Wave;
using NAudio.WaveFormRenderer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace NarraVoice.UI.Windows
{
    public partial class VisualizerWindow : Window
    {
        private readonly string _audioPath;
        private AudioAnalysisResult? _result;
        private int _currentPage = 0;
        private int _pageCount = 1;
        private int _panelWidth = 1060;

        /// <summary>Seconds of audio per page. Tune 6–12 as you prefer.</summary>
        private const double PageSeconds = 8.0;
        private const double MinLastPageSeconds = 4.0; // fold leftovers shorter than this

        private const int WaveformHeight = 180;
        private const int PitchHeight = 150;
        private const int EnergyHeight = 130;
        private const int Dpi = 96;

        private static readonly Color PitchColor = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color EnergyColor = Color.FromRgb(0xF4, 0x43, 0x36);
        private static readonly Color GridColor = Color.FromArgb(40, 0, 0, 0);
        private static readonly Color BackgroundColor = Color.FromRgb(0xFF, 0xFF, 0xFF);

        public VisualizerWindow(
            string audioPath,
            List<(double Time, string Text)> segmentBoundaryTimes,
            List<SegmentTiming> segmentTimings,
            Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _audioPath = audioPath;
            FileLabel.Text = $"Analyzing: {Path.GetFileName(audioPath)}";
            StatusLabel.Text = "Analyzing audio...";
            // segmentBoundaryTimes / segmentTimings kept for call-site compatibility only
            _ = LoadAnalysisAsync();
        }

        private async Task LoadAnalysisAsync()
        {
            try
            {
                _result = await Task.Run(() => AudioAnalysisService.Analyze(_audioPath));
                _panelWidth = Math.Max(800, (int)ActualWidth - 20);
                RecalcPageCount();
                _currentPage = 0;
                await RenderCurrentPageAsync();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusLabel.Text = $"Error: {ex.Message}";
                    MessageBox.Show($"Visualizer error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void RecalcPageCount()
        {
            if (_result == null || _result.TotalDuration <= 0)
            {
                _pageCount = 1;
                return;
            }

            double total = _result.TotalDuration;
            int full = (int)(total / PageSeconds);
            double remainder = total - full * PageSeconds;

            if (full == 0)
                _pageCount = 1;
            else if (remainder < MinLastPageSeconds)
                _pageCount = full; // last "page" absorbs the short tail
            else
                _pageCount = full + 1;
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_result == null) return;
            _panelWidth = Math.Max(800, (int)ActualWidth - 20);
            _ = RenderCurrentPageAsync();
        }

        private void OnPrevSegment(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                _ = RenderCurrentPageAsync();
            }
        }

        private void OnNextSegment(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _pageCount - 1)
            {
                _currentPage++;
                _ = RenderCurrentPageAsync();
            }
        }

        private async Task RenderCurrentPageAsync()
        {
            if (_result == null) return;

            double totalDuration = _result.TotalDuration;
            double pageStart = _currentPage * PageSeconds;

            double pageEnd = Math.Min(pageStart + PageSeconds, totalDuration);
            double pageDuration = Math.Max(0.001, pageEnd - pageStart);

            int pw = _panelWidth;

            var waveformBitmap = await Task.Run(() =>
                BuildWaveformBitmap(pageStart, pageEnd, pageDuration, pw));
            var pitchBitmap = await Task.Run(() =>
                BuildPitchBitmap(pageStart, pageEnd, pageDuration, pw));
            var energyBitmap = await Task.Run(() =>
                BuildEnergyBitmap(pageStart, pageEnd, pageDuration, pw));

            if (_currentPage >= _pageCount - 1)
                pageEnd = totalDuration; // include any remainder
            else
                pageEnd = pageStart + PageSeconds;

            pageEnd = Math.Min(pageEnd, totalDuration);

            Dispatcher.Invoke(() =>
            {
                WaveformImage.Source = waveformBitmap;
                PitchImage.Source = pitchBitmap;
                EnergyImage.Source = energyBitmap;

                SegmentCountLabel.Text =
                    $"Page {_currentPage + 1} of {_pageCount} ({pageStart:F1}–{pageEnd:F1}s)";

                StatusLabel.Text =
                    $"File: {totalDuration:F2}s total | This page: {pageDuration:F2}s";

                PrevBtn.IsEnabled = _currentPage > 0;
                NextBtn.IsEnabled = _currentPage < _pageCount - 1;
            });
        }

        // ── Waveform ─────────────────────────────────────────────────────────
        private BitmapSource BuildWaveformBitmap(
            double segStart, double segEnd, double segDuration, int width)
        {
            if (_result == null) return CreateEmptyBitmap(width, WaveformHeight);

            int startSample = (int)(segStart * _result.SampleRate);
            int endSample = (int)(segEnd * _result.SampleRate);
            startSample = Math.Clamp(startSample, 0, _result.Waveform.Length);
            endSample = Math.Clamp(endSample, startSample, _result.Waveform.Length);
            float[] segSamples = _result.Waveform[startSample..endSample];

            string tempPath = Path.Combine(
                AppConfig.TempDir, $"nv_viz_{Guid.NewGuid()}.wav");
            try
            {
                WriteSegmentWav(segSamples, (int)_result.SampleRate, tempPath);
                System.Drawing.Bitmap? waveformBmp = null;
                try
                {
                    var renderer = new WaveFormRenderer();
                    var peakProvider = new RmsPeakProvider(200);
                    var settings = new StandardWaveFormRendererSettings
                    {
                        Width = width,
                        TopHeight = WaveformHeight / 2,
                        BottomHeight = WaveformHeight / 2,
                        BackgroundColor = System.Drawing.Color.White,
                        TopPeakPen = new System.Drawing.Pen(
                            System.Drawing.Color.FromArgb(0x21, 0x96, 0xF3), 1),
                        BottomPeakPen = new System.Drawing.Pen(
                            System.Drawing.Color.FromArgb(0x64, 0xB5, 0xF6), 1),
                    };
                    using var reader = new AudioFileReader(tempPath);
                    waveformBmp = (System.Drawing.Bitmap)renderer.Render(
                        reader, peakProvider, settings);
                    var wpfBitmap = ConvertBitmap(waveformBmp);
                    return OverlayWaveform(wpfBitmap, segDuration, width, segStart);
                }
                finally
                {
                    waveformBmp?.Dispose();
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }

        private BitmapSource OverlayWaveform(
            BitmapSource source, double duration, int width, double segStart)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(source, new Rect(0, 0, width, WaveformHeight));
                DrawGridLines(dc, duration, width, WaveformHeight);
                // Silence shading removed — detector was inconsistent with speech
                DrawTimeAxis(dc, duration, width, WaveformHeight);
            }
            return RenderVisual(visual, width, WaveformHeight);
        }

        private static void DrawSilenceShading(
            DrawingContext dc,
            List<SilenceRegion> silenceRegions,
            double segStart, double segEnd,
            double duration, int width)
        {
            if (silenceRegions == null || silenceRegions.Count == 0) return;
            var shadeBrush = new SolidColorBrush(Color.FromArgb(60, 0xFF, 0xC1, 0x07));
            shadeBrush.Freeze();
            foreach (var region in silenceRegions)
            {
                if (region.EndTime <= segStart || region.StartTime >= segEnd) continue;
                double clampedStart = Math.Max(region.StartTime, segStart);
                double clampedEnd = Math.Min(region.EndTime, segEnd);
                double x1 = (clampedStart - segStart) / duration * width;
                double x2 = (clampedEnd - segStart) / duration * width;
                dc.DrawRectangle(shadeBrush, null,
                    new Rect(x1, 0, Math.Max(1, x2 - x1), WaveformHeight));
            }
        }

        // ── Pitch ────────────────────────────────────────────────────────────
        private BitmapSource BuildPitchBitmap(
            double segStart, double segEnd, double segDuration, int width)
        {
            if (_result == null) return CreateEmptyBitmap(width, PitchHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                DrawBackground(dc, width, PitchHeight);
                DrawGridLines(dc, segDuration, width, PitchHeight);
                const float pitchMin = 75f;
                const float pitchMax = 400f;
                var highConfPen = new Pen(new SolidColorBrush(PitchColor), 2.0);
                highConfPen.Freeze();
                var lowConfPen = new Pen(new SolidColorBrush(
                    Color.FromArgb(120, PitchColor.R, PitchColor.G, PitchColor.B)), 1.0)
                {
                    DashStyle = DashStyles.Dot
                };
                lowConfPen.Freeze();

                Point? lastPoint = null;
                bool lastHighConf = true;
                for (int i = 0; i < _result.PitchContour.Length; i++)
                {
                    double t = _result.PitchTimes[i];
                    if (t < segStart || t > segEnd) continue;
                    float pitch = _result.PitchContour[i];
                    float confidence = _result.PitchConfidence[i];
                    if (float.IsNaN(pitch) || pitch <= 0)
                    {
                        lastPoint = null;
                        continue;
                    }
                    double x = (t - segStart) / segDuration * width;
                    double y = PitchHeight -
                        (pitch - pitchMin) / (pitchMax - pitchMin) * PitchHeight;
                    y = Math.Clamp(y, 0, PitchHeight);
                    var current = new Point(x, y);
                    bool highConf = confidence > 0.3f;
                    if (lastPoint.HasValue)
                    {
                        var pen = highConf && lastHighConf ? highConfPen : lowConfPen;
                        dc.DrawLine(pen, lastPoint.Value, current);
                    }
                    lastPoint = current;
                    lastHighConf = highConf;
                }
                DrawYLabels(dc, PitchHeight, pitchMin, pitchMax, "Hz");
                DrawTimeAxis(dc, segDuration, width, PitchHeight);
            }
            return RenderVisual(visual, width, PitchHeight);
        }

        // ── Energy ───────────────────────────────────────────────────────────
        private BitmapSource BuildEnergyBitmap(
            double segStart, double segEnd, double segDuration, int width)
        {
            if (_result == null) return CreateEmptyBitmap(width, EnergyHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                DrawBackground(dc, width, EnergyHeight);
                DrawGridLines(dc, segDuration, width, EnergyHeight);

                float maxRms = 0.01f;
                for (int i = 0; i < _result.RmsTimes.Length; i++)
                    if (_result.RmsTimes[i] >= segStart &&
                        _result.RmsTimes[i] <= segEnd &&
                        _result.RmsEnergy[i] > maxRms)
                        maxRms = _result.RmsEnergy[i];

                var fillBrush = new SolidColorBrush(
                    Color.FromArgb(60, EnergyColor.R, EnergyColor.G, EnergyColor.B));
                fillBrush.Freeze();
                var linePen = new Pen(new SolidColorBrush(EnergyColor), 1.5);
                linePen.Freeze();

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    bool started = false;
                    double lastX = 0;
                    for (int i = 0; i < _result.RmsEnergy.Length; i++)
                    {
                        double t = _result.RmsTimes[i];
                        if (t < segStart || t > segEnd) continue;
                        double x = (t - segStart) / segDuration * width;
                        double y = EnergyHeight -
                            (_result.RmsEnergy[i] / maxRms) * EnergyHeight;
                        y = Math.Clamp(y, 0, EnergyHeight);
                        if (!started)
                        {
                            ctx.BeginFigure(new Point(x, EnergyHeight), true, true);
                            ctx.LineTo(new Point(x, y), false, false);
                            started = true;
                        }
                        else
                        {
                            ctx.LineTo(new Point(x, y), true, false);
                        }
                        lastX = x;
                    }
                    if (started)
                        ctx.LineTo(new Point(lastX, EnergyHeight), true, false);
                }
                geometry.Freeze();
                dc.DrawGeometry(fillBrush, linePen, geometry);
                DrawYLabels(dc, EnergyHeight, 0f, maxRms, "RMS");
                DrawTimeAxis(dc, segDuration, width, EnergyHeight);
            }
            return RenderVisual(visual, width, EnergyHeight);
        }

        // ── Shared helpers ───────────────────────────────────────────────────
        private static void DrawBackground(DrawingContext dc, int width, int height)
        {
            dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null,
                new Rect(0, 0, width, height));
        }

        private static void DrawGridLines(DrawingContext dc, double duration,
            int width, int height)
        {
            var pen = new Pen(new SolidColorBrush(GridColor), 0.5);
            pen.Freeze();
            double step = duration > 30 ? 1.0 : duration > 10 ? 0.5 : 0.125;
            for (double t = step; t < duration; t += step)
            {
                double x = t / duration * width;
                dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
            }
            dc.DrawLine(pen, new Point(0, height / 2.0), new Point(width, height / 2.0));
        }

        private static void DrawYLabels(DrawingContext dc, int height,
            float min, float max, string unit)
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            var typeface = new Typeface("Segoe UI");
            void DrawLabel(float value, double y)
            {
                var ft = new FormattedText(
                    $"{value:F2}{unit}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface, 9, brush,
                    VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);
                dc.DrawText(ft, new Point(2, y - ft.Height / 2));
            }
            DrawLabel(max, 4);
            DrawLabel((min + max) / 2, height / 2.0);
            DrawLabel(min, height - 4);
        }

        private static void DrawTimeAxis(DrawingContext dc, double duration,
            int width, int height)
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            var typeface = new Typeface("Segoe UI");
            double step = duration > 30 ? 5.0 : duration > 10 ? 1.0 : 0.25;
            for (double t = 0; t <= duration; t += step)
            {
                double x = t / duration * width;
                var ft = new FormattedText(
                    $"{t:F1}s",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface, 9, brush,
                    VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);
                dc.DrawText(ft, new Point(x + 1, height - ft.Height - 1));
            }
        }

        private static BitmapSource RenderVisual(DrawingVisual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(
                width, height, Dpi, Dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            memory.Position = 0;
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        private static BitmapSource CreateEmptyBitmap(int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            return RenderVisual(visual, width, height);
        }

        private static void WriteSegmentWav(float[] samples, int sampleRate, string path)
        {
            int byteCount = samples.Length * 2;
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + byteCount);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((ushort)1);
            bw.Write((ushort)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((ushort)2);
            bw.Write((ushort)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(byteCount);
            foreach (var s in samples)
            {
                short pcm = (short)Math.Clamp(s * 32768f, short.MinValue, short.MaxValue);
                bw.Write(pcm);
            }
        }
    }
}