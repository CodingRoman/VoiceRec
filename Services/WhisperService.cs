using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Whisper.net;

namespace VoiceRec.Services;

public class WhisperService : IDisposable
{
    private bool _isInitialized;
    private string? _modelPath;
    private bool _disposed;
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisperProcessor;
    private static readonly HttpClient _httpClient = new HttpClient();

    public bool IsInitialized => _isInitialized;
    public string? ModelPath => _modelPath;

    public event EventHandler<string>? StatusChanged;

    // Model URL and filename
    private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin";
    private const string ModelFileName = "ggml-small.bin";

    public async Task<bool> InitializeAsync(string? customModelPath = null)
    {
        try
        {
            // Debug: Show current directory and base directory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exeLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var currentDir = Directory.GetCurrentDirectory();

            StatusChanged?.Invoke(this, $"Debug - BaseDir: {baseDir}");
            await Task.Delay(50);
            StatusChanged?.Invoke(this, $"Debug - ExeLocation: {exeLocation}");
            await Task.Delay(50);
            StatusChanged?.Invoke(this, $"Debug - CurrentDir: {currentDir}");
            await Task.Delay(50);

            StatusChanged?.Invoke(this, "Checking for Whisper model...");

            // Get the executable directory - try multiple methods
            var exeDir = GetExecutableDirectory();

            // Check if custom path is provided
            if (!string.IsNullOrEmpty(customModelPath) && File.Exists(customModelPath))
            {
                _modelPath = customModelPath;
                StatusChanged?.Invoke(this, $"Using custom model path: {_modelPath}");
            }
            else
            {
                // Check if model exists in various paths
                var modelPaths = new List<string>
                {
                    // First check current directory (where the app was started)
                    Path.Combine(currentDir, "Models", ModelFileName),
                    Path.Combine(currentDir, ModelFileName),
                    // Then check executable directory
                    Path.Combine(exeDir, "Models", ModelFileName),
                    Path.Combine(exeDir, ModelFileName),
                    // AppData locations
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceRec", "Models", ModelFileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRec", "Models", ModelFileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "whisper", ModelFileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whisper", ModelFileName),
                };

                StatusChanged?.Invoke(this, $"Debug - ExeDir: {exeDir}");
                await Task.Delay(50);

                foreach (var path in modelPaths)
                {
                    var exists = File.Exists(path);
                    StatusChanged?.Invoke(this, $"Debug - Check: {path} = {exists}");
                    await Task.Delay(20);

                    if (exists)
                    {
                        _modelPath = path;
                        StatusChanged?.Invoke(this, $"Found model at: {path}");
                        break;
                    }
                }
            }

            // If still not found, offer to download
            if (string.IsNullOrEmpty(_modelPath))
            {
                StatusChanged?.Invoke(this, "Whisper model not found. Would you like to download it?");
                StatusChanged?.Invoke(this, "Downloading Whisper model...");

                var downloadSuccess = await DownloadModelAsync();
                if (!downloadSuccess)
                {
                    StatusChanged?.Invoke(this, "Failed to download Whisper model. Please download manually.");
                    return false;
                }

                // Try to find the newly downloaded model
                _modelPath = Path.Combine(exeDir, "Models", ModelFileName);
                if (!File.Exists(_modelPath))
                {
                    _modelPath = Path.Combine(currentDir, "Models", ModelFileName);
                }
                if (!File.Exists(_modelPath))
                {
                    _modelPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRec", "Models", ModelFileName);
                }
            }

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                StatusChanged?.Invoke(this, "Whisper model not found. Please check debug output above.");
                return false;
            }

            StatusChanged?.Invoke(this, $"Loading Whisper model from {_modelPath}...");

            // Load the Whisper model using the factory
            // The second parameter enables GPU - we try with false first to use CPU only
            // This avoids OpenVINO dependency issues
            // Load Whisper model using just the path (uses default options)
            _whisperFactory = await Task.Run(() => WhisperFactory.FromPath(_modelPath));

            // Create a processor with language detection
            var builder = _whisperFactory.CreateBuilder()
                .WithLanguage("auto");

            // Get the builder back and configure sampling
            var samplingBuilder = builder.WithGreedySamplingStrategy();
            _whisperProcessor = samplingBuilder.ParentBuilder.Build();
            
            _isInitialized = true;
            StatusChanged?.Invoke(this, "Whisper model loaded successfully");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to initialize Whisper: {ex.Message}");
            _isInitialized = false;
            return false;
        }
    }

    public async Task<string?> TranscribeAsync(string audioFilePath)
    {
        if (!_isInitialized || _whisperProcessor == null)
        {
            return "Whisper not initialized. Please ensure the model is installed.";
        }

        if (!File.Exists(audioFilePath))
        {
            return $"Audio file not found: {audioFilePath}";
        }

        try
        {
            StatusChanged?.Invoke(this, "Transcribing audio...");

            string fullTranscript = "";

            // ProcessAsync accepts a Stream
            using var fileStream = File.OpenRead(audioFilePath);
            await foreach (var result in _whisperProcessor.ProcessAsync(fileStream))
            {
                fullTranscript = result.Text;
            }

            // Add punctuation
            fullTranscript = AddPunctuation(fullTranscript);

            StatusChanged?.Invoke(this, "Transcription complete");
            return string.IsNullOrWhiteSpace(fullTranscript) ? "No speech detected" : fullTranscript;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Transcription error: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string?> TranscribeAsync(byte[] audioData)
    {
        if (!_isInitialized || _whisperFactory == null)
        {
            return "Whisper not initialized. Please ensure the model is installed.";
        }

        if (audioData.Length == 0)
        {
            return "No audio data to transcribe";
        }

        try
        {
            // Convert bytes to float array (16-bit PCM)
            float[] floatData = ConvertToFloatAudio(audioData);
            
            // Process the audio data directly
            string fullTranscript = "";
            
            await foreach (var result in _whisperProcessor.ProcessAsync(floatData))
            {
                fullTranscript = result.Text;
            }

            // Add punctuation
            fullTranscript = AddPunctuation(fullTranscript);

            StatusChanged?.Invoke(this, "Transcription complete");
            return string.IsNullOrWhiteSpace(fullTranscript) ? "No speech detected" : fullTranscript;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Transcription error: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Converts 16-bit PCM bytes to float array
    /// </summary>
    private float[] ConvertToFloatAudio(byte[] pcmData)
    {
        // Check if it's a WAV file
        if (pcmData.Length >= 44 && System.Text.Encoding.ASCII.GetString(pcmData, 0, 4).Equals("RIFF"))
        {
            // Skip WAV header (44 bytes) and get audio data
            int dataSize = pcmData.Length - 44;
            float[] floatData = new float[dataSize / 2];
            
            for (int i = 0; i < floatData.Length; i++)
            {
                short sample = (short)(pcmData[44 + i * 2] | (pcmData[44 + i * 2 + 1] << 8));
                floatData[i] = sample / 32768f;
            }
            
            return floatData;
        }
        
        // Raw PCM - assume 16-bit mono
        float[] result = new float[pcmData.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            short sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
            result[i] = sample / 32768f;
        }
        return result;
    }

    private string AddPunctuation(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Basic punctuation addition
        text = text.Trim();
        
        // Add period at end if no punctuation
        if (!text.EndsWith('.') && !text.EndsWith('?') && !text.EndsWith('!') && !text.EndsWith(','))
        {
            text += ".";
        }

        // Capitalize first letter
        if (text.Length > 0)
        {
            text = char.ToUpper(text[0]) + text.Substring(1);
        }

        return text;
    }

    public static string GetInstallationInstructions()
    {
        return @"# Whisper Model Installation Anleitung

Um die Spracherkennung nutzen zu können, müssen Sie ein Whisper-Modell herunterladen.

## Option 1: Direkter Download (empfohlen)

Laden Sie das kleine Modell (empfohlen, ca. 465 MB) herunter:

1. Erstellen Sie einen Ordner namens ""Models"" neben der VoiceRec.exe
   - Oder: %LOCALAPPDATA%\VoiceRec\Models\

2. Laden Sie das Modell herunter von:
   https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin

3. Benennen Sie die Datei in ""ggml-small.bin"" um und speichern Sie sie im Models-Ordner.

## Option 2: Direkt im EXE-Ordner

Legen Sie die Datei ggml-small.bin direkt neben VoiceRec.exe.

## Verfügbare Modelle (in Reihenfolge der Größe):

- tiny: ~39 MB - schnell, weniger genau
- base: ~75 MB - gute Balance
- small: ~465 MB - besser (empfohlen)
- medium: ~769 MB - sehr gut
- large: ~1550 MB - beste Qualität

## Nach dem Download:

Starten Sie die Anwendung neu. Das Modell wird automatisch erkannt.";
    }

    /// <summary>
    /// Gets the directory where the executable is located
    /// </summary>
    private string GetExecutableDirectory()
    {
        // Try multiple methods to get the executable directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
        {
            return baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // Try getting the location of the executing assembly
        var exeLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(exeLocation))
        {
            var dir = Path.GetDirectoryName(exeLocation);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        // Fallback to current directory
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Downloads the Whisper model automatically
    /// </summary>
    private async Task<bool> DownloadModelAsync()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var exeDir = GetExecutableDirectory();

            // Try multiple download locations
            var downloadPaths = new[]
            {
                Path.Combine(exeDir, "Models"),
                Path.Combine(currentDir, "Models"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRec", "Models"),
            };

            string? targetDir = null;
            foreach (var path in downloadPaths)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    targetDir = path;
                    break;
                }
                catch
                {
                    // Try next path
                }
            }

            if (targetDir == null)
            {
                targetDir = Path.Combine(exeDir, "Models");
                Directory.CreateDirectory(targetDir);
            }

            var targetPath = Path.Combine(targetDir, ModelFileName);

            StatusChanged?.Invoke(this, $"Downloading model to: {targetPath}");
            StatusChanged?.Invoke(this, "This may take a few minutes (approx. 465 MB)...");

            // Download the model
            using var response = await _httpClient.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var canReportProgress = totalBytes != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (canReportProgress)
                {
                    var progress = (double)totalRead / totalBytes * 100;
                    StatusChanged?.Invoke(this, $"Downloading... {progress:F1}% ({totalRead / 1024 / 1024} MB / {totalBytes / 1024 / 1024} MB)");
                }
            }

            StatusChanged?.Invoke(this, $"Model downloaded successfully to: {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Download failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _whisperProcessor?.Dispose();
        _whisperFactory?.Dispose();
        
        _disposed = true;
    }
}
