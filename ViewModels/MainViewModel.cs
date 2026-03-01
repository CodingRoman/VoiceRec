using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VoiceRec.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VoiceRec.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioRecordingService _audioService;
    private readonly WhisperService _whisperService;
    private readonly TranslationService _translationService;
    private bool _disposed;

    [ObservableProperty]
    private string _transcribedText = "";

    [ObservableProperty]
    private string _translatedText = "";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private bool _isWhisperInitialized;

    [ObservableProperty]
    private string _statusMessage = "Bereit";

    [ObservableProperty]
    private string _whisperStatus = "Whisper nicht installiert";

    [ObservableProperty]
    private string _installationGuide = "";

    [ObservableProperty]
    private bool _showInstallationGuide;

    [ObservableProperty]
    private double _currentAudioLevel;

    [ObservableProperty]
    private ISeries[] _waveformSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] _xAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _yAxes = Array.Empty<Axis>();

    private readonly ObservableCollection<ObservableValue> _waveformValues = new();

    public MainViewModel()
    {
        _audioService = new AudioRecordingService();
        _whisperService = new WhisperService();
        _translationService = new TranslationService();

        // Setup waveform chart
        SetupWaveformChart();

        // Subscribe to events
        _audioService.AudioLevelChanged += OnAudioLevelChanged;
        _audioService.SilenceDetected += OnSilenceDetected;
        _audioService.RecordingStarted += (s, e) => UpdateStatus("Aufnahme läuft...");
        _audioService.RecordingStopped += (s, e) => UpdateStatus("Aufnahme beendet");
        _whisperService.StatusChanged += OnWhisperStatusChanged;

        // Show installation guide
        InstallationGuide = WhisperService.GetInstallationInstructions();
    }

    private void SetupWaveformChart()
    {
        _xAxes = new Axis[]
        {
            new Axis
            {
                IsVisible = false,
                ShowSeparatorLines = false
            }
        };

        _yAxes = new Axis[]
        {
            new Axis
            {
                IsVisible = false,
                MinLimit = -1,
                MaxLimit = 1,
                ShowSeparatorLines = false
            }
        };

        WaveformSeries = new ISeries[]
        {
            new LineSeries<ObservableValue>
            {
                Values = _waveformValues,
                Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(100)),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            }
        };
    }

    private void OnAudioLevelChanged(object? sender, float[] samples)
    {
        // Update chart with audio samples
        Dispatcher.UIThread.Post(() =>
        {
            _waveformValues.Clear();
            
            // Downsample for display
            int step = Math.Max(1, samples.Length / 500);
            for (int i = 0; i < samples.Length; i += step)
            {
                if (_waveformValues.Count < 500)
                {
                    _waveformValues.Add(new ObservableValue(samples[i]));
                }
            }

            // Calculate RMS for display
            float sum = 0;
            foreach (var s in samples)
                sum += s * s;
            CurrentAudioLevel = Math.Sqrt(sum / samples.Length) * 100;
        });
    }

    private void UpdateStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = message;
        });
    }

    private async void OnSilenceDetected(object? sender, string message)
    {
        UpdateStatus("Stille erkannt - Transkription wird gestartet...");
        
        // Stop recording and transcribe
        _audioService.StopRecording();
        
        await TranscribeAudioCommand.ExecuteAsync(null);
    }

    private void OnWhisperStatusChanged(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            WhisperStatus = status;
        });
    }

    [ObservableProperty]
    private string _ollamaStatus = "Ollama nicht installiert";

    [ObservableProperty]
    private bool _isOllamaAvailable;

    public async Task InitializeAsync()
    {
        // Try to initialize Whisper
        var success = await _whisperService.InitializeAsync();
        
        IsWhisperInitialized = success;
        ShowInstallationGuide = !success;
        
        if (!success)
        {
            UpdateStatus("Whisper nicht gefunden. Bitte installieren Sie das Modell.");
        }

        // Check Ollama availability
        var ollamaAvailable = await _translationService.CheckOllamaAsync();
        IsOllamaAvailable = ollamaAvailable;
        
        if (ollamaAvailable)
        {
            OllamaStatus = "Ollama bereit";
            var models = await _translationService.GetAvailableModelsAsync();
            if (models.Length > 0)
            {
                OllamaStatus = $"Ollama bereit ({models.Length} Modelle)";
            }
        }
        else
        {
            OllamaStatus = "Ollama nicht verfügbar - nur Offline-Übersetzung";
        }
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (IsRecording)
        {
            _audioService.StopRecording();
            IsRecording = false;
            UpdateStatus("Aufnahme gestoppt");
        }
        else
        {
            if (!IsWhisperInitialized)
            {
                UpdateStatus("Whisper nicht initialisiert. Bitte Modell installieren.");
                return;
            }

            _audioService.StartRecording();
            IsRecording = true;
            TranscribedText = "";
            TranslatedText = "";
        }
    }

    [RelayCommand]
    private async Task TranscribeAudio()
    {
        if (IsTranscribing)
            return;

        IsTranscribing = true;
        UpdateStatus("Transkribiere...");

        try
        {
            var audioBytes = _audioService.GetRecordingBytes();
            
            if (audioBytes.Length == 0)
            {
                UpdateStatus("Keine Audio-Daten vorhanden");
                IsTranscribing = false;
                return;
            }

            var result = await _whisperService.TranscribeAsync(audioBytes);
            
            if (!string.IsNullOrEmpty(result))
            {
                TranscribedText = result;
                UpdateStatus("Transkription abgeschlossen");
            }
            else
            {
                UpdateStatus("Keine Transkription möglich");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Fehler: {ex.Message}");
        }
        finally
        {
            IsTranscribing = false;
            IsRecording = false;
        }
    }

    [RelayCommand]
    private async Task TranslateText()
    {
        if (string.IsNullOrWhiteSpace(TranscribedText))
        {
            UpdateStatus("Kein Text zum Übersetzen");
            return;
        }

        UpdateStatus("Übersetze auf Englisch...");

        try
        {
            TranslatedText = await _translationService.TranslateToEnglishAsync(TranscribedText);
            UpdateStatus("Übersetzung abgeschlossen");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Übersetzungsfehler: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CopyText()
    {
        if (!string.IsNullOrEmpty(TranscribedText))
        {
            // Use Avalonia's clipboard
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topWindow = desktop.MainWindow;
                if (topWindow != null)
                {
                    topWindow.Clipboard?.SetTextAsync(TranscribedText);
                    UpdateStatus("Text in Zwischenablage kopiert");
                }
            }
        }
    }

    [RelayCommand]
    private void CopyTranslation()
    {
        if (!string.IsNullOrEmpty(TranslatedText))
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topWindow = desktop.MainWindow;
                if (topWindow != null)
                {
                    topWindow.Clipboard?.SetTextAsync(TranslatedText);
                    UpdateStatus("Übersetzung in Zwischenablage kopiert");
                }
            }
        }
    }

    [RelayCommand]
    private void ClearText()
    {
        TranscribedText = "";
        TranslatedText = "";
        UpdateStatus("Text gelöscht");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _audioService.AudioLevelChanged -= OnAudioLevelChanged;
        _audioService.SilenceDetected -= OnSilenceDetected;
        _whisperService.StatusChanged -= OnWhisperStatusChanged;

        _audioService.Dispose();
        _whisperService.Dispose();
        _translationService.Dispose();

        _disposed = true;
    }
}
