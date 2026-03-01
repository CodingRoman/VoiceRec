using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRec.Services;

public class AudioRecordingService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _recordingStream;
    private WaveFileWriter? _waveWriter;
    private bool _isRecording;
    private readonly List<float> _audioSamples = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _silenceDetectionCts;
    private float _lastAudioLevel;
    private bool _isSilent;
    private const int SilenceThreshold = 50; // RMS threshold for silence detection (lower = more sensitive)
    private const int SilenceDurationMs = 5000; // Duration of silence to trigger transcription (5 seconds)

    public event EventHandler<float[]>? AudioLevelChanged;
    public event EventHandler<string>? SilenceDetected;
    public event EventHandler<string>? RecordingStarted;
    public event EventHandler<string>? RecordingStopped;

    public bool IsRecording => _isRecording;
    public List<float> AudioSamples => _audioSamples;

    public void StartRecording()
    {
        if (_isRecording) return;

        try
        {
            _recordingStream = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono for Whisper
            };

            _waveWriter = new WaveFileWriter(_recordingStream, _waveIn.WaveFormat);

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            _isRecording = true;
            
            lock (_lock)
            {
                _audioSamples.Clear();
            }

            // Start silence detection
            _silenceDetectionCts = new CancellationTokenSource();
            _ = DetectSilenceAsync(_silenceDetectionCts.Token);

            RecordingStarted?.Invoke(this, "Recording started");
        }
        catch (Exception ex)
        {
            StopRecording();
            RecordingStopped?.Invoke(this, $"Error: {ex.Message}");
        }
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        _silenceDetectionCts?.Cancel();
        _isRecording = false;

        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            // Ignore
        }
    }

    public string? GetRecordingFilePath()
    {
        if (_recordingStream == null || _recordingStream.Length == 0)
            return null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"voice_rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        
        // Save the memory stream to a file
        _recordingStream.Position = 0;
        using var fileStream = File.Create(tempPath);
        _recordingStream.CopyTo(fileStream);
        
        return tempPath;
    }

    public byte[] GetRecordingBytes()
    {
        if (_recordingStream == null)
            return Array.Empty<byte>();
        
        _recordingStream.Position = 0;
        return _recordingStream.ToArray();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_waveWriter != null)
        {
            _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
        }

        // Calculate audio levels for visualization
        var samples = new float[e.BytesRecorded / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
            samples[i] = sample / 32768f;
        }

        // Calculate RMS
        float sum = 0;
        foreach (var s in samples)
            sum += s * s;
        float rms = (float)Math.Sqrt(sum / samples.Length) * 100;

        _lastAudioLevel = rms;

        lock (_lock)
        {
            // Add samples for visualization (downsampled)
            foreach (var s in samples)
            {
                _audioSamples.Add(s);
            }
            
            // Keep only last 2000 samples for visualization
            if (_audioSamples.Count > 2000)
            {
                _audioSamples.RemoveRange(0, _audioSamples.Count - 2000);
            }
        }

        AudioLevelChanged?.Invoke(this, samples);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _waveWriter?.Dispose();
        _waveWriter = null;

        _waveIn?.Dispose();
        _waveIn = null;

        _recordingStream?.Dispose();
        _recordingStream = null;

        RecordingStopped?.Invoke(this, "Recording stopped");
    }

    private async Task DetectSilenceAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRecording)
        {
            await Task.Delay(100, ct);

            if (_lastAudioLevel < SilenceThreshold)
            {
                if (!_isSilent)
                {
                    _isSilent = true;
                    _silenceStartTime = DateTime.Now;
                }
                else if ((DateTime.Now - _silenceStartTime).TotalMilliseconds > SilenceDurationMs)
                {
                    // Silence for more than 2 seconds - trigger transcription
                    SilenceDetected?.Invoke(this, "Silence detected - starting transcription");
                    break;
                }
            }
            else
            {
                _isSilent = false;
            }
        }
    }

    private DateTime _silenceStartTime;

    public void Dispose()
    {
        StopRecording();
        _silenceDetectionCts?.Dispose();
        _waveWriter?.Dispose();
        _waveIn?.Dispose();
        _recordingStream?.Dispose();
    }
}
