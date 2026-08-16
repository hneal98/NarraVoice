// MainWindow.xaml.cs
// Main window code-behind for NarraVoice.
// Coordinates all UI interactions, project management,
// chunk editing, rendering, and voice management.

using NarraVoice.Core.Config;
using NarraVoice.Core.Engine;
using NarraVoice.Core.IPA;
using NarraVoice.Core.Models;
using NarraVoice.Core.Services;
using NarraVoice.Editor.Controls;
using NarraVoice.UI.Dialogs;
using NarraVoice.UI.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NarraVoice.UI.Windows
{
    public partial class MainWindow : Window
    {
        // ── Services ──────────────────────────────────────────────────────────

        // NEW: (KokoroSharp)
        private readonly RenderPipeline _pipeline;
        private readonly SubstitutionService _substitutions;
        private readonly VoiceManagerService _voiceManager;
        private readonly IpaLookupService _ipaService;
        private readonly AudioPlayerService _player;
        private List<(double Time, string Text)> _lastSegmentBoundaryTimes = new();
        private List<SegmentTiming> _lastSegmentTimings = new();
        
        // ── Project state ─────────────────────────────────────────────────────

        private string? _projectDir;
        private string _currentInstruct = "";
        private ProjectConfig _projectConfig = new();
        private ChunkAssignments _chunkAssignments = new();
        private List<string> _chunkFiles = new();
        private readonly PresetGutter Gutter = new();
        private int _currentChunkIndex = -1;
        private bool _isRendering;
        
        // ── Render cancellation ───────────────────────────────────────────────

        private CancellationTokenSource? _cts;

        // ── Commands (for key bindings) ───────────────────────────────────────

        public ICommand SaveChunkCommand { get; }
        public ICommand RenderChunkCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand BatchRenderCommand { get; }
        public ICommand PrevChunkCommand { get; }
        public ICommand NextChunkCommand { get; }
        public ICommand OpenScratchpadCommand { get; }
        public ICommand ResumeCommand { get; }
        public ICommand RestoreChunkCommand { get; }
        public ICommand VisualizeCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        public MainWindow()
        {
            // Initialize services
            _substitutions = new SubstitutionService();
            _voiceManager = new VoiceManagerService();
            _ipaService = new IpaLookupService();
            _player = new AudioPlayerService();
            _pipeline = new RenderPipeline(_substitutions);


            // Wire commands
            SaveChunkCommand = new RelayCommand(() => OnSaveChunk(null, null));
            RenderChunkCommand = new RelayCommand(() => OnRenderChunk(null, null),
                                        () => !_isRendering && _currentChunkIndex >= 0);
            PreviewCommand = new RelayCommand(() => OnPreview(null, null),
                                        () => !_isRendering && _currentChunkIndex >= 0);
            BatchRenderCommand = new RelayCommand(() => OnBatchRender(null, null),
                                        () => !_isRendering && _chunkFiles.Count > 0);
            PrevChunkCommand = new RelayCommand(() => OnPrevChunk(null, null),
                                        () => _currentChunkIndex > 0);
            NextChunkCommand = new RelayCommand(() => OnNextChunk(null, null),
                                        () => _currentChunkIndex < _chunkFiles.Count - 1);
            OpenScratchpadCommand = new RelayCommand(() => OnOpenScratchpad(null, null));
            ResumeCommand = new RelayCommand(() => OnResume(null, null));
            RestoreChunkCommand = new RelayCommand(() => OnRestoreChunk(null, null));
            VisualizeCommand = new RelayCommand(() => OnVisualize(null, null));
            ZoomInCommand = new RelayCommand(() => AdjustUIZoom(0.1));
            ZoomOutCommand = new RelayCommand(() => AdjustUIZoom(-0.1));
            PreviewKeyDown += OnWindowKeyDown;
            

            InitializeComponent();

            ChunkEditor.Substitutions = _substitutions.Substitutions;

            DataContext = this;

            // Add gutter as a margin on the editor's text area
            ChunkEditor.TextArea.LeftMargins.Add(Gutter);
            Gutter.MarkersChanged += OnMarkersChanged;

            // Initialize engine in background
            _ = InitializeEngineAsync();

            // Load last project
            var config = ProjectManager.LoadNarrationConfig();
            if (!string.IsNullOrEmpty(config.LastProject) &&
                Directory.Exists(config.LastProject))
            {
                _ = LoadProjectAsync(config.LastProject);
            }
        }

        // ── Engine initialization ─────────────────────────────────────────────

        

        private bool _engineInitialized;

        private async Task InitializeEngineAsync()
        {
            if (_engineInitialized) return;
            _engineInitialized = true;
            try
            {
                AppendLog($"VoicesDir = {AppConfig.VoicesDir}");
                AppendLog($"Exists = {Directory.Exists(AppConfig.VoicesDir)}");

                var voices = _voiceManager.GetAvailableVoices();
                PopulateVoiceDropdown(voices);
                AppendLog($"Loaded {voices.Count} Kokoro voices.");
            }
            catch (Exception ex)
            {
                AppendLog($"Engine initialization failed: {ex.Message}");
            }
        }

        // ── Project management ────────────────────────────────────────────────

        private async void OnNewProject(object sender, RoutedEventArgs e)
        {
            // Simple input dialog for project name
            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter project name:", "New Project", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            string slug = ProjectManager.Slugify(name);
            string projDir = Path.Combine(AppConfig.ProjectsDir, slug);

            if (Directory.Exists(projDir))
            {
                MessageBox.Show($"A project named '{name}' already exists.",
                    "Project Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProjectManager.SetupProjectDirectories(projDir);

            var cfg = new ProjectConfig
            {
                Name = name,
                Slug = slug,
                Created = DateTime.Now.ToString("yyyy-MM-dd"),
            };
            ProjectManager.SaveProject(projDir, cfg);
            await LoadProjectAsync(projDir);
            AppendLog($"Created project: {name}");

            // Immediately open story file picker
            await SelectStoryFilesAsync();
        }


        private async void OnMergeSelectedFiles(object sender, RoutedEventArgs e)
        {
            var dlg = new MergeFilesDialog { Owner = this };
            if (dlg.ShowDialog() != true || !dlg.Confirmed) return;

            string outputFilename = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a name for the merged file (no extension):",
                "Merge Files", "merged_output");
            if (string.IsNullOrWhiteSpace(outputFilename)) return;

            var selected = dlg.OrderedSelectedFiles;
            AppendLog($"Merging {selected.Count} selected file(s)...");

            string mergedPath = await _pipeline.MergeChunksAsync(
                selected, dlg.SelectedFolder!, $"{outputFilename}.mp3", AppendLog);

            if (!string.IsNullOrEmpty(mergedPath))
                AppendLog($"Merged file created: {Path.GetFileName(mergedPath)}");
        }

        /// <summary>
        /// Let user select multiple story files for the project.
        /// All selected files are copied to temp_stories/ and first is chunked.
        /// </summary>
        private async Task SelectStoryFilesAsync()
        {
            if (_projectDir == null)
            {
                MessageBox.Show("Please create or open a project first.",
                    "No Project", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Story Files",
                Filter = "Word documents (*.docx)|*.docx|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1,
                Multiselect = true,
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                AppendLog($"Loading {dlg.FileNames.Length} story file(s)...");
                await CopyStoryFilesToTempAsync(dlg.FileNames.ToList());
            }
            catch (Exception ex)
            {
                AppendLog($"Error loading files: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy selected story files to temp_stories/ and chunk the first one.
        /// </summary>
        private async Task CopyStoryFilesToTempAsync(List<string> filePaths)
        {
            if (_projectDir == null) return;

            string tempStoriesDir = ProjectManager.TempStoriesDir(_projectDir);
            Directory.CreateDirectory(tempStoriesDir);

            _projectConfig.StoryFiles.Clear();
            _projectConfig.CurrentStoryIndex = 0;

            // Copy all files to temp_stories/
            foreach (string filePath in filePaths)
            {
                string filename = Path.GetFileName(filePath);
                string destPath = Path.Combine(tempStoriesDir, filename);
                await Task.Run(() => File.Copy(filePath, destPath, overwrite: true));
                _projectConfig.StoryFiles.Add(filename);
                AppendLog($"Copied: {filename}");
            }

            ProjectManager.SaveProject(_projectDir, _projectConfig);

            // Chunk the first story
            await ChunkCurrentStoryAsync();
        }

        /// <summary>
        /// Chunk the current story file (from temp_stories/) into chunks folder.
        /// </summary>
        private async Task ChunkCurrentStoryAsync()
        {
            if (_projectDir == null || _projectConfig.StoryFiles.Count == 0) return;

            string currentStoryFile = _projectConfig.StoryFiles[_projectConfig.CurrentStoryIndex];
            string storyPath = Path.Combine(ProjectManager.TempStoriesDir(_projectDir), currentStoryFile);

            if (!File.Exists(storyPath))
            {
                AppendLog($"Story file not found: {currentStoryFile}");
                return;
            }

            try
            {
                AppendLog($"Chunking story {_projectConfig.CurrentStoryIndex + 1} of {_projectConfig.StoryFiles.Count}: {currentStoryFile}");
                string text = await Task.Run(() => ExtractText(storyPath));
                await CreateChunksAsync(text);
            }
            catch (Exception ex)
            {
                AppendLog($"Error chunking story: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a story is complete (audiobook created).
        /// Deletes the story file from temp_stories, deletes chunks, loads next story.
        /// </summary>
        private async Task CompleteCurrentStoryAsync()
        {
            if (_projectDir == null || _projectConfig.StoryFiles.Count == 0) return;

            string currentStoryFile = _projectConfig.StoryFiles[_projectConfig.CurrentStoryIndex];
            string storyPath = Path.Combine(ProjectManager.TempStoriesDir(_projectDir), currentStoryFile);

            // Delete the temp story file
            try
            {
                if (File.Exists(storyPath))
                    File.Delete(storyPath);
                AppendLog($"Deleted story: {currentStoryFile}");
            }
            catch (Exception ex)
            {
                AppendLog($"Warning: Could not delete story file: {ex.Message}");
            }

            // Delete all chunk files
            string chunksDir = ProjectManager.ChunksDir(_projectDir);
            try
            {
                foreach (var chunkFile in _chunkFiles)
                {
                    if (File.Exists(chunkFile))
                        File.Delete(chunkFile);
                }
                AppendLog("Deleted chunk files.");
            }
            catch (Exception ex)
            {
                AppendLog($"Warning: Could not delete chunk files: {ex.Message}");
            }

            _chunkFiles.Clear();
            _currentChunkIndex = -1;

            // Move to next story
            _projectConfig.CurrentStoryIndex++;

            if (_projectConfig.CurrentStoryIndex < _projectConfig.StoryFiles.Count)
            {
                ProjectManager.SaveProject(_projectDir, _projectConfig);
                await ChunkCurrentStoryAsync();
                UpdateChunkStatus();
            }
            else
            {
                AppendLog("All stories complete!");
                _projectConfig.CurrentStoryIndex = 0;
                ProjectManager.SaveProject(_projectDir, _projectConfig);
            }
        }

        private async void OnOpenStoryFile(object sender, RoutedEventArgs e)
        {
            await SelectStoryFilesAsync();
        }

        private void OnResumeFromFolder(object sender, RoutedEventArgs e)
        {
            // Use OpenFileDialog pointed at a folder as WPF alternative
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a project folder — choose any file inside it",
                CheckFileExists = false,
                FileName = "Select Folder",
                Filter = "Project folder|*.json",
            };
            if (dlg.ShowDialog() != true) return;
            string folder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            if (!string.IsNullOrEmpty(folder))
                _ = LoadProjectAsync(folder);
        }

        private async Task LoadProjectAsync(string projDir)
        {
            _projectDir = projDir;
            _projectConfig = ProjectManager.LoadProject(projDir);
            _chunkAssignments = ProjectManager.LoadChunkAssignments(projDir);

            // Load chunk files
            string chunksDir = ProjectManager.ChunksDir(projDir);
            _chunkFiles = Directory.Exists(chunksDir)
                ? Directory.GetFiles(chunksDir, "*.txt")
                    .OrderBy(f => f).ToList()
                : new List<string>();

            // Update UI
            Title = $"NarraVoice — {_projectConfig.Name}";
            UpdateMenuState(true);
            EnableProjectControls(true);

            // Load first unrendered chunk or last chunk
            if (_chunkFiles.Count > 0)
            {
                int startIndex = FindResumeIndex();
                await LoadChunkAsync(startIndex);
            }

            UpdateChunkStatus();

            // Save as last project
            var narConfig = new NarrationConfig { LastProject = projDir };
            ProjectManager.SaveNarrationConfig(narConfig);

            AppendLog($"Loaded project: {_projectConfig.Name} " +
                      $"({_chunkFiles.Count} chunks)");

            // Apply voice settings
            ApplyVoiceSettings();
            // Restore last selected preset
            if (!string.IsNullOrEmpty(_projectConfig.Preset))
            {
                for (int i = 0; i < PresetCombo.Items.Count; i++)
                {
                    if (PresetCombo.Items[i] is ComboBoxItem item &&
                        item.Tag as string == _projectConfig.Preset)
                    {
                        PresetCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Always arm the gutter with whatever preset is selected
            ArmGutter();
        }

        private double _uiScale = 1.0;
        private void AdjustUIZoom(double delta)
        {
            _uiScale = Math.Clamp(_uiScale + delta, 0.7, 1.5);
            Application.Current.MainWindow.FontSize = 12 * _uiScale;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    AdjustUIZoom(0.1);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    AdjustUIZoom(-0.1);
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    Gutter.Scale += 0.1;
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    Gutter.Scale -= 0.1;
                    e.Handled = true;
                }
            }
        }
        private void OnRenameProject(object sender, RoutedEventArgs e)
        {
            if (_projectDir == null) return;
            string newName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter new project name:", "Rename Project", _projectConfig.Name);
            if (string.IsNullOrWhiteSpace(newName)) return;

            _projectConfig.Name = newName;
            _projectConfig.Slug = ProjectManager.Slugify(newName);
            ProjectManager.SaveProject(_projectDir, _projectConfig);
            Title = $"NarraVoice — {newName}";
            AppendLog($"Project renamed to: {newName}");
        }

        private void OnDeleteProject(object sender, RoutedEventArgs e)
        {
            if (_projectDir == null) return;
            var result = MessageBox.Show(
                $"Permanently delete project '{_projectConfig.Name}' and all its files?",
                "Delete Project", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                Directory.Delete(_projectDir, recursive: true);
                _projectDir = null;
                _projectConfig = new ProjectConfig();
                _chunkFiles = new List<string>();
                _currentChunkIndex = -1;
                Title = "NarraVoice";
                EnableProjectControls(false);
                UpdateMenuState(false);
                ChunkEditor.LoadText(string.Empty);
                AppendLog("Project deleted.");
            }
            catch (Exception ex)
            {
                AppendLog($"Error deleting project: {ex.Message}");
            }
        }

        private void OnOpenAudioFolder(object sender, RoutedEventArgs e)
        {
            if (_projectDir == null) return;
            string audioDir = ProjectManager.AudioDir(_projectDir);
            Directory.CreateDirectory(audioDir);
            Process.Start("explorer.exe", audioDir);
        }

        private void OnOpenAudiobookFolder(object sender, RoutedEventArgs e)
        {
            if (_projectDir == null) return;
            string audiobooksDir = ProjectManager.AudiobooksDir(_projectDir);
            Directory.CreateDirectory(audiobooksDir);
            Process.Start("explorer.exe", audiobooksDir);
        }

        private void OnExit(object sender, RoutedEventArgs e) => Close();

        // ── Chunk management ──────────────────────────────────────────────────

        private async Task CreateChunksAsync(string text)
        {
            if (_projectDir == null) return;

            string chunksDir = ProjectManager.ChunksDir(_projectDir);
            Directory.CreateDirectory(chunksDir);

            // Clear existing chunks
            foreach (var f in Directory.GetFiles(chunksDir, "*.txt"))
                File.Delete(f);

            // Split into chunks (~2500 chars at sentence boundaries)
            var chunks = SplitIntoChunks(text, 2500);
            AppendLog($"Split story {_projectConfig.CurrentStoryIndex + 1} into {chunks.Count} chunks.");

            string slug = _projectConfig.Slug;
            _chunkFiles.Clear();

            for (int i = 0; i < chunks.Count; i++)
            {
                string filename = Path.Combine(chunksDir,
                    $"{slug}_{i + 1:D4}.txt");
                await File.WriteAllTextAsync(filename, chunks[i]);
                _chunkFiles.Add(filename);

                // Save original — never overwritten
                string origPath = $"{filename}.orig";
                if (!File.Exists(origPath))
                    await File.WriteAllTextAsync(origPath, chunks[i]);
            }

            _chunkAssignments = new ChunkAssignments();
            await LoadChunkAsync(0);
            UpdateChunkStatus();
        }

        private async Task LoadChunkAsync(int index)
        {
            if (index < 0 || index >= _chunkFiles.Count) return;

            // Auto-save current chunk if modified
            if (_currentChunkIndex >= 0 && ChunkEditor.HasUnsavedChanges)
                await SaveCurrentChunkAsync(silent: true);

            _currentChunkIndex = index;
            string text = await File.ReadAllTextAsync(_chunkFiles[index]);
            AppendLog($"Chunk {index + 1}: {text.Length} chars, {text.Count(c => c == '\n')} newlines");
            Dispatcher.Invoke(() => ChunkEditor.LoadText(text));


            // Load gutter markers for this chunk
            var markers = _chunkAssignments
                .GetPresetChanges(index + 1)
                .Select(pc => new GutterMarker
                {
                    Line = pc.Line,
                    PresetName = pc.Preset,
                    Color = GetPresetColor(pc.Preset),
                })
                .ToList();
            Dispatcher.Invoke(() => Gutter.SetMarkers(markers));

            UpdateChunkStatus();
        }

        private async Task SaveCurrentChunkAsync(bool silent = false)
        {
            if (_currentChunkIndex < 0 || _projectDir == null) return;

            string path = _chunkFiles[_currentChunkIndex];
            ProjectManager.RotateBackups(path);
            await File.WriteAllTextAsync(path, ChunkEditor.Text);
            ChunkEditor.MarkSaved();

            if (!silent)
                AppendLog($"Saved chunk {_currentChunkIndex + 1:D4}");
        }

        private async void OnSaveChunk(object? sender, RoutedEventArgs? e)
        {
            await SaveCurrentChunkAsync();
        }

        private async void OnRestoreChunk(object? sender, RoutedEventArgs? e)
        {
            if (_currentChunkIndex < 0) return;
            string path = _chunkFiles[_currentChunkIndex];
            string origPath = $"{path}.orig";
            if (!File.Exists(origPath))
            {
                MessageBox.Show("No original found for this chunk.",
                    "Restore Chunk", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            string text = await File.ReadAllTextAsync(origPath);
            ChunkEditor.LoadText(text);
            AppendLog($"Restored chunk {_currentChunkIndex + 1:D4} from original.");
        }

        private async void OnPrevChunk(object? sender, RoutedEventArgs? e)
        {
            if (_currentChunkIndex > 0)
                await LoadChunkAsync(_currentChunkIndex - 1);
        }

        private async void OnNextChunk(object? sender, RoutedEventArgs? e)
        {
            if (_currentChunkIndex < _chunkFiles.Count - 1)
                await LoadChunkAsync(_currentChunkIndex + 1);
        }

        private async void OnResume(object? sender, RoutedEventArgs? e)
        {
            if (_projectDir == null) return;
            string audioDir = ProjectManager.AudioDir(_projectDir);
            int resumeIndex = FindResumeIndex();
            await LoadChunkAsync(resumeIndex);
        }

        private int FindResumeIndex()
        {
            if (_projectDir == null) return 0;
            string audioDir = ProjectManager.AudioDir(_projectDir);
            string slug = _projectConfig.Slug;

            for (int i = 0; i < _chunkFiles.Count; i++)
            {
                string wav = Path.Combine(audioDir, $"{slug}_{i + 1:D4}.wav");
                if (!File.Exists(wav)) return i;
            }
            return 0;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private async void OnRenderChunk(object? sender, RoutedEventArgs? e)
        {
            if (_currentChunkIndex < 0 || _projectDir == null || _isRendering) return;

            await SaveCurrentChunkAsync(silent: true);
            SetRendering(true);

            _cts = new CancellationTokenSource();

            try
            {
                string audioDir = ProjectManager.AudioDir(_projectDir);
                Directory.CreateDirectory(audioDir);

                var presetChanges = _chunkAssignments
                    .GetPresetChanges(_currentChunkIndex + 1);

                var result = await _pipeline.RenderChunkAsync(
                    ChunkEditor.Text,
                    GetCurrentProfile(),
                    audioDir,
                    _currentChunkIndex + 1,
                    _projectConfig.Slug,
                    presetChanges,
                    _projectConfig.Presets,
                    AppendLog,
                    _cts.Token);

                if (result.Success)
                {
                    _player.Load(result.Mp3Path, autoplay: true);
                    UpdateChunkStatus();

                    // Auto-advance to next chunk
                    if (_currentChunkIndex < _chunkFiles.Count - 1)
                        await LoadChunkAsync(_currentChunkIndex + 1);
                    else
                        await TriggerMergeAsync();
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("Render cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"Render error: {ex.Message}");
            }
            finally
            {
                SetRendering(false);
            }
        }

        private async void OnBatchRender(object? sender, RoutedEventArgs? e)
        {
            if (_projectDir == null || _isRendering) return;

            int remaining = _chunkFiles.Count - _currentChunkIndex;
            if (remaining <= 0) return;

            var result = MessageBox.Show(
                $"Render the remaining {remaining} chunk(s)?",
                "Batch Render", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            SetRendering(true);
            _cts = new CancellationTokenSource();
            AppendLog("Batch rendering started...");

            try
            {
                string audioDir = ProjectManager.AudioDir(_projectDir);
                Directory.CreateDirectory(audioDir);

                for (int i = _currentChunkIndex; i < _chunkFiles.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    await LoadChunkAsync(i);
                    await SaveCurrentChunkAsync(silent: true);

                    var presetChanges = _chunkAssignments.GetPresetChanges(i + 1);

                    await _pipeline.RenderChunkAsync(
                        ChunkEditor.Text,
                        GetCurrentProfile(),
                        audioDir,
                        i + 1,
                        _projectConfig.Slug,
                        presetChanges,
                        _projectConfig.Presets,
                        AppendLog,
                        _cts.Token);
                }

                AppendLog("Batch rendering complete.");
                UpdateChunkStatus();
                await TriggerMergeAsync();

                // After merge, complete the current story and load next
                await CompleteCurrentStoryAsync();
            }
            catch (OperationCanceledException)
            {
                AppendLog("Batch render cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"Batch render error: {ex.Message}");
            }
            finally
            {
                SetRendering(false);
            }
        }


        private async void OnPreview(object? sender, RoutedEventArgs? e)
        {
            // Prevent multiple rapid clicks / double execution
            if (_currentChunkIndex < 0 || _isRendering)
                return;

            AppendLog($"Preview clicked at {DateTime.Now:HH:mm:ss.fff}");   // ← new line, with milliseconds

            // Cancel any previous preview operation
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            string text;
            List<PresetChange> presetChanges;

            if (ChunkEditor.SelectionLength > 0)
            {
                text = ChunkEditor.SelectedText;
                var (startLine, endLine) = ChunkEditor.GetSelectionLineRange();
                presetChanges = GetPresetChangesForSelection(startLine, endLine);
            }
            else
            {
                text = ChunkEditor.Text;
                presetChanges = _chunkAssignments.GetPresetChanges(_currentChunkIndex + 1);
            }

            if (string.IsNullOrWhiteSpace(text))
                return;

            SetRendering(true);

            try
            {
                string tmpDir = Path.Combine(Path.GetTempPath(), "NarraVoice");
                Directory.CreateDirectory(tmpDir);

                // === CRITICAL: Fully release previous audio file ===
                //_player.Unload();
                await Task.Delay(50);                    // Give OS time to release file handle

                var result = await _pipeline.RenderChunkAsync(
                    text,
                    GetCurrentProfile(),
                    tmpDir,
                    chunkIndex: -1,
                    prefix: _projectConfig.Slug,
                    presetChanges: presetChanges,
                    presetsLibrary: _projectConfig.Presets,
                    log: AppendLog,
                    cancellationToken: _cts.Token);

                if (result.Success)
                {
                    _lastSegmentBoundaryTimes = result.SegmentBoundaryTimes;
                    _lastSegmentTimings = result.SegmentTimings;

                    AppendLog($"Preview complete — {_lastSegmentBoundaryTimes.Count} segment boundaries captured.");
                }

                //if (result.Success && File.Exists(result.Mp3Path))
                //{
                //    _player.Load(result.Mp3Path, autoplay: true);
                //}
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling previous preview
            }
            catch (Exception ex)
            {
                AppendLog($"Preview error: {ex.Message}");
            }
            finally
            {
                SetRendering(false);
            }
        }

        private VoiceProfile GetCurrentProfile()
        {
            string voiceId = (VoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string
                             ?? "am_fenrir";

            var profile = new VoiceProfile(voiceId, RateLabel.Text, PitchLabel.Text, VolumeLabel.Text);

            if (!string.IsNullOrWhiteSpace(_currentInstruct))
                profile.Instruct = _currentInstruct.Trim();

            AppendLog($"Profile: voice={profile.Voice} engine={profile.Engine} pitch={profile.Pitch}" +
                      (string.IsNullOrEmpty(profile.Instruct) ? "" : $" instruct=\"{profile.Instruct}\""));
            return profile;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _pipeline.StopPlayback();
            CancelBtn.Content = "Canceled!!!";
            CancelBtn.IsEnabled = false;
        }

        private void OnVoiceBlend(object sender, RoutedEventArgs e)
        {
            var voices = _voiceManager.GetAvailableVoices();
            var dlg = new VoiceBlenderDialog(_pipeline.GetSharedTts(), voices, this);
            if (dlg.ShowDialog() == true)
            {
                // Reload voice list to include the new blended voice
                var updatedVoices = _voiceManager.GetAvailableVoices();
                PopulateVoiceDropdown(updatedVoices);
                AppendLog("Voice blend saved — voice list refreshed.");
            }
        }


        private async Task TriggerMergeAsync()
        {
            if (_projectDir == null) return;
            string audioDir = ProjectManager.AudioDir(_projectDir);
            string slug = _projectConfig.Slug;
            string audiobooksDir = ProjectManager.AudiobooksDir(_projectDir);
            Directory.CreateDirectory(audiobooksDir);

            var wavs = Directory.GetFiles(audioDir, $"{slug}_????.wav")
                .OrderBy(f => f).ToList();

            if (wavs.Count == 0) return;

            AppendLog($"Merging {wavs.Count} chunk(s) into audiobook...");
            string outFilename = $"{slug}_story_{_projectConfig.CurrentStoryIndex + 1}_audiobook.mp3";

            string mergedPath = await _pipeline.MergeChunksAsync(
                wavs, audiobooksDir, outFilename, AppendLog);

            if (!string.IsNullOrEmpty(mergedPath))
                AppendLog($"Audiobook created: {Path.GetFileName(mergedPath)}");
        }

        // ── Playback ──────────────────────────────────────────────────────────

        private void OnPlay(object sender, RoutedEventArgs e)
        {
            _pipeline.PlayLastPreview();
        }

        private void OnPauseResume(object sender, RoutedEventArgs e)
        {
            _pipeline.TogglePause();
            PauseResumeBtn.Content = _pipeline.IsCurrentlyPaused()
                ? "▶ Resume"
                : "⏸ Pause";
        }



        private void OnVisualize(object? sender, RoutedEventArgs? e)
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "NarraVoice");
            string previewPath = Path.Combine(tmpDir,
                $"{_projectConfig.Slug}_preview.wav");

            var launcher = new VisualizeLaunchDialog(previewPath, this);

            // Disable Preview button if no preview file exists
            launcher.SetPreviewAvailable(File.Exists(previewPath));

            if (launcher.ShowDialog() != true || string.IsNullOrEmpty(launcher.SelectedPath))
                return;

            bool isPreview = launcher.SelectedPath == previewPath;
            var boundaryTimes = isPreview ? _lastSegmentBoundaryTimes : new List<(double Time, string Text)>();

            var timings = isPreview ? _lastSegmentTimings : new List<SegmentTiming>();
            var win = new VisualizerWindow(launcher.SelectedPath, boundaryTimes, timings, this);
            win.Show();
        }

        private void OnStop(object sender, RoutedEventArgs e) => _pipeline.StopPlayback();

        // ── Voice and presets ─────────────────────────────────────────────────

        private void PopulateVoiceDropdown(List<(string Id, string Label)> voices)
        {
            Dispatcher.Invoke(() =>
            {
                VoiceCombo.Items.Clear();

                // Reformat: "American English — Female — Heart" 
                //        → display "Heart — American English — Female"
                //        sort by original "American English — Female" (description first)
                var formatted = voices.Select(v =>
                {
                    var parts = v.Label.Split(" — ");
                    string name = parts.Length > 0 ? parts[^1] : v.Label;
                    string description = parts.Length > 1
                        ? string.Join(" — ", parts[..^1])
                        : string.Empty;
                    string display = string.IsNullOrEmpty(description)
                        ? name
                        : $"{name} — {description}";
                    return (Id: v.Id, Display: display, SortKey: $"{description} — {name}");
                })
                .OrderBy(v => v.SortKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

                foreach (var (id, display, _) in formatted)
                {
                    VoiceCombo.Items.Add(new ComboBoxItem
                    {
                        Content = display,
                        Tag = id,
                        IsEnabled = true,
                    });
                }

                // Select project voice or first item
                bool found = false;
                if (!string.IsNullOrEmpty(_projectConfig.Voice))
                {
                    for (int i = 0; i < VoiceCombo.Items.Count; i++)
                    {
                        if (VoiceCombo.Items[i] is ComboBoxItem item &&
                            item.Tag as string == _projectConfig.Voice)
                        {
                            VoiceCombo.SelectedIndex = i;
                            found = true;
                            break;
                        }
                    }
                }
                if (!found && VoiceCombo.Items.Count > 0)
                    VoiceCombo.SelectedIndex = 0;
            });
        }
        private void OnVoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            string voiceId = (VoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "af_heart";
            _ipaService.SetLanguageFromVoice(voiceId);
            ArmGutter();
            UpdateInstructButtonsVisibility();

            bool isQwen = voiceId.StartsWith("qwen_", StringComparison.OrdinalIgnoreCase);
            InstructButton.Visibility = isQwen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnInstructClick(object sender, RoutedEventArgs e)
        {
            var dlg = new InstructDialog(_currentInstruct) { Owner = this };
            if (dlg.ShowDialog() == true)
                _currentInstruct = dlg.InstructText;
        }

        private void OnRateChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (RateLabel != null)
                RateLabel.Text = $"{(int)e.NewValue:+0;-0;+0}%";
        }

        private void OnPitchChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchLabel != null)
                PitchLabel.Text = $"{e.NewValue:+0.##;-0.##;+0}st";
        }
        private void OnVolumeChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeLabel != null)
                VolumeLabel.Text = $"{(int)e.NewValue}%";
        }

        private void OnNeutral(object sender, RoutedEventArgs e)
        {
            RateSlider.Value = 0;
            PitchSlider.Value = 0;
            VolumeSlider.Value = 100;
        }

        private string GeneratePresetColor()
        {
            // Generate evenly spaced HSL colors using an ever-incrementing index
            // so deleted presets don't cause color reuse/collisions
            int index = _projectConfig.PresetColorIndex;
            _projectConfig.PresetColorIndex++;

            double hue = (index * 137.508) % 360; // Golden angle spacing
            var color = HslToRgb(hue, 0.65, 0.55);
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetCombo.SelectedItem is not ComboBoxItem item) return;

            string presetName = item.Tag as string ?? string.Empty;
            if (string.IsNullOrEmpty(presetName)) return;

            // Save selected preset to project config
            if (_projectDir != null)
            {
                _projectConfig.Preset = presetName;
                ProjectManager.SaveProject(_projectDir, _projectConfig);
            }

            // __none__ — leave Voice/Rate/Pitch/Volume sliders as-is,
            // disarm the gutter so clicking does nothing
            if (presetName == "__none__")
            {
                ArmGutter();
                UpdateInstructButtonsVisibility();
                return;
            }

            // Apply preset settings if preset exists
            if (_projectConfig.Presets.TryGetValue(presetName, out var preset))
            {
                if (int.TryParse(preset.Rate.Replace("%", ""), out int rate))
                    RateSlider.Value = rate;
                if (float.TryParse(preset.Pitch.Replace("st", ""), out float pitch))
                    PitchSlider.Value = pitch;
                if (int.TryParse(preset.Volume.Replace("%", ""), out int volume))
                    VolumeSlider.Value = volume;

                for (int i = 0; i < VoiceCombo.Items.Count; i++)
                {
                    if (VoiceCombo.Items[i] is ComboBoxItem vi &&
                        vi.Tag as string == preset.Voice)
                    {
                        VoiceCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            ArmGutter();
        }

        private void OnPresetInstructClick(object sender, RoutedEventArgs e)
        {
            string? presetName = (PresetCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(presetName) || presetName == "__none__") return;
            if (_projectConfig == null || !_projectConfig.Presets.TryGetValue(presetName, out var preset))
                return;

            var dlg = new InstructDialog(preset.Instruct ?? "") { Owner = this };
            if (dlg.ShowDialog() != true) return;

            preset.Instruct = dlg.InstructText?.Trim() ?? "";
            if (!string.IsNullOrEmpty(_projectDir))
                ProjectManager.SaveProject(_projectDir, _projectConfig);

            AppendLog($"Preset '{presetName}' instruct updated.");
        }

        private void OnSavePreset(object sender, RoutedEventArgs e)
        {
            string currentPreset = (PresetCombo.SelectedItem as ComboBoxItem)?.Tag as string
                ?? "__none__";

            string name;

            if (currentPreset != "__none__")
            {
                // A preset is selected — offer to update it in place
                var result = MessageBox.Show(
                    $"Update preset '{currentPreset}' with the current settings?",
                    "Save Preset", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;

                if (result == MessageBoxResult.Yes)
                {
                    name = currentPreset;
                }
                else
                {
                    // No — save to a different preset (existing or new)
                    string existingNames = string.Join(", ", _projectConfig.Presets.Keys);
                    name = Microsoft.VisualBasic.Interaction.InputBox(
                        $"Enter a preset name to save to.\n" +
                        $"Type an existing name to overwrite it, or a new name to create one.\n\n" +
                        $"Existing presets: {existingNames}",
                        "Save Preset", "");
                    if (string.IsNullOrWhiteSpace(name)) return;
                }
            }
            else
            {
                // __none__ — must create a new preset
                name = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter preset name:", "Save Preset", "");
                if (string.IsNullOrWhiteSpace(name)) return;
            }

            string voiceId = (VoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string
                ?? "af_heart";

            // Preserve existing color if updating an existing preset, otherwise generate a new one
            string color = _projectConfig.Presets.TryGetValue(name, out var existing)
                ? existing.Color
                : GeneratePresetColor();

            var preset = new Preset(
                name, voiceId, RateLabel.Text, PitchLabel.Text,
                color, VolumeLabel.Text);

            if (!string.IsNullOrWhiteSpace(_currentInstruct))
                preset.Instruct = _currentInstruct.Trim();
            else if (_projectConfig.Presets.TryGetValue(name, out var existingPreset))
                preset.Instruct = existingPreset.Instruct;  // keep old instruct when updating if dialog is empty

            _projectConfig.Presets[name] = preset;
            ProjectManager.SaveProject(_projectDir!, _projectConfig);
            RefreshPresetCombo();

            // Select the saved preset in the dropdown
            for (int i = 0; i < PresetCombo.Items.Count; i++)
            {
                if (PresetCombo.Items[i] is ComboBoxItem item &&
                    item.Tag as string == name)
                {
                    PresetCombo.SelectedIndex = i;
                    break;
                }
            }

            ArmGutter();
            AppendLog($"Preset '{name}' saved.");
        }
        private void OnDeletePreset(object sender, RoutedEventArgs e)
        {
            if (PresetCombo.SelectedItem is not ComboBoxItem item) return;
            string presetName = item.Tag as string ?? string.Empty;
            if (string.IsNullOrEmpty(presetName)) return;

            _projectConfig.Presets.Remove(presetName);
            ProjectManager.SaveProject(_projectDir!, _projectConfig);
            RefreshPresetCombo();
            AppendLog($"Preset '{presetName}' deleted.");
        }

        private void UpdateInstructButtonsVisibility()
        {
            string voiceId = (VoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            bool isQwenVoice = voiceId.StartsWith("qwen_", StringComparison.OrdinalIgnoreCase);
            InstructButton.Visibility = isQwenVoice ? Visibility.Visible : Visibility.Collapsed;

            string? presetName = (PresetCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            bool showPreset = !string.IsNullOrEmpty(presetName)
                              && presetName != "__none__"
                              && _projectConfig.Presets.TryGetValue(presetName, out var p)
                              && p.Voice.StartsWith("qwen_", StringComparison.OrdinalIgnoreCase);

            PresetInstructButton.Visibility = showPreset ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshPresetCombo()
        {
            PresetCombo.Items.Clear();

            // __none__ option — uses the current Voice/Rate/Pitch/Volume sliders
            // directly, with no gutter preset armed.
            PresetCombo.Items.Add(new ComboBoxItem
            {
                Content = "__none__",
                Tag = "__none__",
            });

            foreach (var kvp in _projectConfig.Presets)
            {
                // Parse the hex color
                var color = System.Windows.Media.Colors.Gray;
                try
                {
                    color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString(kvp.Value.Color);
                }
                catch { }

                // Build a colored dot + name panel
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Margin = new Thickness(0, 0, 6, 0),
                    Fill = new System.Windows.Media.SolidColorBrush(color),
                };
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = kvp.Key,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var panel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                };
                panel.Children.Add(dot);
                panel.Children.Add(label);

                PresetCombo.Items.Add(new ComboBoxItem
                {
                    Content = panel,
                    Tag = kvp.Key,
                });
            }
        }
        private string GetPresetColor(string presetName)
        {
            return _projectConfig.Presets.TryGetValue(presetName, out var p)
                ? p.Color : "#808080";
        }

        private void ArmGutter()
        {
            if (PresetCombo.SelectedItem is ComboBoxItem item)
            {
                string name = item.Tag as string ?? string.Empty;

                // __none__ disarms the gutter — clicking does nothing
                if (name == "__none__")
                {
                    Gutter.ArmPreset(string.Empty, "#808080");
                    return;
                }

                string color = GetPresetColor(name);
                Gutter.ArmPreset(name, color);
            }
        }

        private void ApplyVoiceSettings()
        {
            // Apply project default voice settings to sliders
            for (int i = 0; i < VoiceCombo.Items.Count; i++)
            {
                if (VoiceCombo.Items[i] is ComboBoxItem item &&
                    item.Tag as string == _projectConfig.Voice)
                {
                    VoiceCombo.SelectedIndex = i;
                    break;
                }
            }

            if (int.TryParse(_projectConfig.Rate.Replace("%", ""), out int rate))
                RateSlider.Value = rate;
            if (int.TryParse(_projectConfig.Pitch.Replace("st", ""), out int pitch))
                PitchSlider.Value = pitch;
            if (int.TryParse(_projectConfig.Volume.Replace("%", ""), out int volume))
                VolumeSlider.Value = volume;

            RefreshPresetCombo();
        }


        // ── Auto color generation ─────────────────────────────────────────────

        private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        // ── Gutter ────────────────────────────────────────────────────────────

        private List<PresetChange> GetPresetChangesForSelection(int startLine, int endLine)
        {
            var allChanges = _chunkAssignments.GetPresetChanges(_currentChunkIndex + 1);
            var result = new List<PresetChange>();
            foreach (var change in allChanges)
            {
                if (change.Line >= startLine && change.Line <= endLine)
                {
                    result.Add(new PresetChange(
                        change.Line - (startLine - 1),
                        change.Preset));
                }
            }
            return result;
        }
        private void OnMarkersChanged(object? sender, List<GutterMarker> markers)
        {
            if (_currentChunkIndex < 0 || _projectDir == null) return;

            var changes = markers.Select(m => new PresetChange(m.Line, m.PresetName))
                .ToList();
            _chunkAssignments.SetPresetChanges(_currentChunkIndex + 1, changes);
            ProjectManager.SaveChunkAssignments(_projectDir, _chunkAssignments);
        }

        // ── Smart IPA ─────────────────────────────────────────────────────────
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
                string ipa = dlg.SelectedIpa;
                string lower = word.ToLower();
                string capitalized = char.ToUpper(word[0]) + word.Substring(1).ToLower();

                // Only add if not already substituted
                if (!_substitutions.Substitutions.ContainsKey(lower) &&
                    !_substitutions.Substitutions.ContainsKey(word))
                {
                    _substitutions.Set(lower, $"[{lower}]({ipa})");
                    _substitutions.Set(capitalized, $"[{capitalized}]({ipa})");
                    _substitutions.Save();
                    ChunkEditor.Substitutions = _substitutions.Substitutions;
                }
            }
        }

        // ── Document modified ─────────────────────────────────────────────────

        private void OnDocumentModified(object? sender, EventArgs e)
        {
            // Update title bar to show unsaved indicator
            string indicator = ChunkEditor.HasUnsavedChanges ? " •" : "";
            string proj = _projectConfig.Name;
            Title = string.IsNullOrEmpty(proj)
                ? $"NarraVoice{indicator}"
                : $"NarraVoice — {proj}{indicator}";
        }

        // ── Silence tag ───────────────────────────────────────────────────────

        private void OnInsertSilence(object sender, RoutedEventArgs e)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Silence duration (ms):", "Insert Silence", "300");
            if (!int.TryParse(input, out int ms) || ms <= 0) return;
            ChunkEditor.InsertAtCaret($"<sil:{ms}ms>");
        }

        // ── Tools ─────────────────────────────────────────────────────────────

        private void OnOpenSubstitutions(object sender, RoutedEventArgs e)
        {
            var dlg = new SubstitutionDialog(_substitutions, this);
            dlg.ShowDialog();
        }

        private void OnOpenVoiceManager(object sender, RoutedEventArgs e)
        {
            var dlg = new VoiceManagerWindow(_voiceManager, this);
            dlg.PreferencesChanged += (s, ev) =>
            {
                var voices = _voiceManager.GetAvailableVoices();
                PopulateVoiceDropdown(voices);
                AppendLog("Voice list refreshed.");
            };
            dlg.Show();
        }

        private ScratchpadWindow? _scratchpad;
        private void OnOpenScratchpad(object? sender, RoutedEventArgs? e)
        {
            if (_scratchpad?.IsVisible == true)
            {
                _scratchpad.Activate();
                return;
            }

            var voices = _voiceManager.GetAvailableVoices();
            string voiceId = (VoiceCombo.SelectedItem as ComboBoxItem)?.Tag as string
                ?? "af_heart";

            _scratchpad = new ScratchpadWindow(
                _pipeline, _ipaService, voices,
                voiceId, RateLabel.Text, PitchLabel.Text, VolumeLabel.Text);
            _scratchpad.Show();
        }

        // ── UI state helpers ──────────────────────────────────────────────────

        private void SetRendering(bool rendering)
        {
            Dispatcher.Invoke(() =>
            {
                _isRendering = rendering;
                RenderBtn.IsEnabled = !rendering;
                BatchBtn.IsEnabled = !rendering;
                PreviewBtn.IsEnabled = !rendering;
                CancelBtn.IsEnabled = rendering;
            });
        }

        private void EnableProjectControls(bool enabled)
        {
            Dispatcher.Invoke(() =>
            {
                // Chunk-specific buttons — only enabled when project has chunks
                ChunkButtonPanel.IsEnabled = enabled;
                PlaybackPanel.IsEnabled = enabled;
                SilenceBtn.IsEnabled = enabled;

                // Status label
                ChunkStatusLabel.Visibility = enabled
                    ? Visibility.Visible : Visibility.Collapsed;
                NoProjectHint.Visibility = enabled
                    ? Visibility.Collapsed : Visibility.Visible;

                // Preview button is always enabled if there's text
                PreviewBtn.IsEnabled = true;

                // Menu items
                UpdateMenuState(enabled);
            });
        }

        private void UpdateMenuState(bool hasProject)
        {
            RenameProjectMenu.IsEnabled = hasProject;
            DeleteProjectMenu.IsEnabled = hasProject;
            OpenAudioFolderMenu.IsEnabled = hasProject;
            OpenAudiobookFolderMenu.IsEnabled = hasProject;
        }

        private void UpdateChunkStatus()
        {
            if (_projectDir == null || _chunkFiles.Count == 0) return;

            string audioDir = ProjectManager.AudioDir(_projectDir);
            string slug = _projectConfig.Slug;
            int rendered = _chunkFiles.Count(f =>
            {
                int idx = _chunkFiles.IndexOf(f) + 1;
                return File.Exists(Path.Combine(audioDir, $"{slug}_{idx:D4}.wav"));
            });

            int current = _currentChunkIndex + 1;
            int total = _chunkFiles.Count;
            int storyNum = _projectConfig.CurrentStoryIndex + 1;
            int storyTotal = _projectConfig.StoryFiles.Count;

            Dispatcher.Invoke(() =>
            {
                StoryStatusLabel.Text = $"Story {storyNum} of {storyTotal}";
                StoryStatusLabel.Visibility = Visibility.Visible;

                ChunkStatusLabel.Text =
                    $"{_projectConfig.Name} — Chunk {current} of {total} | {rendered} rendered";
                ChunkStatusLabel.Visibility = Visibility.Visible;
            });
        }



        // ── Text extraction ───────────────────────────────────────────────────

        private static string ExtractText(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".txt" => File.ReadAllText(path),
                ".docx" => ExtractDocx(path),
                _ => File.ReadAllText(path),
            };
        }

        private static string ExtractDocx(string path)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument
                    .Open(path, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body != null)
                {
                    foreach (var para in body
                        .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        string text = para.InnerText;
                        if (!string.IsNullOrEmpty(text))
                            sb.Append(text + "\n\n");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading Word document: {ex.Message}",
                    "DOCX Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }

        // ── Chunk splitter ────────────────────────────────────────────────────

        private static List<string> SplitIntoChunks(string text, int maxChars)
        {
            var chunks = new List<string>();

            // Normalize line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Split on paragraph boundaries first (double newlines)
            var paragraphs = text.Split(new[] { "\n\n" },
                StringSplitOptions.RemoveEmptyEntries);

            var current = new System.Text.StringBuilder();

            foreach (var para in paragraphs)
            {
                if (string.IsNullOrEmpty(para)) continue;

                if (current.Length > 0 &&
                    current.Length + para.Length + 2 > maxChars)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                    current.Append("\n\n");
                current.Append(para);
            }

            if (current.Length > 0)
                chunks.Add(current.ToString().TrimEnd());

            return chunks.Count > 0 ? chunks : new List<string> { text };
        }

        // ── Activity log ──────────────────────────────────────────────────────

        private void AppendLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                ActivityLog.AppendText($"[{timestamp}] {msg}\n");
                ActivityLog.ScrollToEnd();
            });
        }

        private void OnClearLog(object sender, RoutedEventArgs e) =>
            ActivityLog.Clear();

        // ── Window closing ────────────────────────────────────────────────────

        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (ChunkEditor.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == MessageBoxResult.Yes)
                    await SaveCurrentChunkAsync();
            }

            _cts?.Cancel();
            _voiceManager.Dispose();
            _pipeline.Dispose();
        }
    }

    // ── RelayCommand ──────────────────────────────────────────────────────────

    /// <summary>Simple ICommand implementation for key bindings.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}