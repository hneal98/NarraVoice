using NarraVoice.Core.Config;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NarraVoice.Core.Services
{
    public class QwenServerManager
    {
        private static readonly Lazy<QwenServerManager> _instance = new(() => new QwenServerManager());
        public static QwenServerManager Instance => _instance.Value;
        private QwenServerManager() { }

        private Process? _serverProcess;
        private readonly object _lock = new();
        private const string ServerUrl = "http://127.0.0.1:8765";
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(180)
        };

        public async Task EnsureRunningAsync(Action<string>? log = null)
        {
            lock (_lock)
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                    return;
            }

            // Check if something's already listening on the port, even if this
            // manager instance doesn't have a handle to it (e.g. after an app restart
            // where the old Python process is still running from before)
            try
            {
                var resp = await httpClient.GetAsync($"{ServerUrl}/health");
                if (resp.IsSuccessStatusCode)
                {
                    log?.Invoke("Qwen server already running (reusing existing instance).");
                    return;
                }
            }
            catch
            {
                // nothing listening yet, proceed to start a new one
            }

            log?.Invoke("Starting Qwen server...");

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                ArgumentList = { AppConfig.QwenServerScript },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            lock (_lock)
            {
                _serverProcess = Process.Start(psi);
            }

            await WaitForHealthyAsync(log);
            log?.Invoke("Qwen server ready.");
        }

        private async Task WaitForHealthyAsync(Action<string>? log, int timeoutSeconds = 300)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                if (_serverProcess != null && _serverProcess.HasExited)
                {
                    string stderr = await _serverProcess.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException(
                        $"Qwen server process exited early. Error output: {stderr}");
                }

                try
                {
                    var resp = await httpClient.GetAsync($"{ServerUrl}/health");
                    if (resp.IsSuccessStatusCode)
                    {
                        log?.Invoke($"Qwen server healthy after {sw.Elapsed.TotalSeconds:F0}s");
                        return;
                    }
                }
                catch { /* not up yet */ }

                if ((int)sw.Elapsed.TotalSeconds % 15 == 0)
                    log?.Invoke($"Waiting for Qwen server... {sw.Elapsed.TotalSeconds:F0}s");

                await Task.Delay(1000);
            }

            throw new TimeoutException(
                $"Qwen server did not become healthy in time ({timeoutSeconds}s).");
        }

        public async Task<(byte[] wavBytes, double generationSeconds)> GenerateAsync(
            string text, string speaker, string? instruct, Action<string>? log = null)
        {
            var sw = Stopwatch.StartNew();

            var payload = new { text, speaker, language = "English", instruct = instruct ?? "" };
            var response = await httpClient.PostAsJsonAsync($"{ServerUrl}/generate", payload);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Qwen server error: {err}");
            }

            var wavBytes = await response.Content.ReadAsByteArrayAsync();
            sw.Stop();
            return (wavBytes, sw.Elapsed.TotalSeconds);
        }

        public void Shutdown()
        {
            lock (_lock)
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    try { _serverProcess.Kill(entireProcessTree: true); }
                    catch { /* already gone */ }
                }
                _serverProcess = null;
            }
        }
    }
}