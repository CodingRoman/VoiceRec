using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const int SilenceDurationMs = 2000; // Duration of silence to trigger transcription (2 seconds)

    public event EventHandler<float[]>? AudioLevelChanged;
    public event EventHandler<string>? SilenceDetected;
    public event EventHandler<string>? RecordingStarted;
    public event EventHandler<string>? RecordingStopped;
    public event EventHandler<string>? RecordingError;

    public bool IsRecording => _isRecording;
    public List<float> AudioSamples => _audioSamples;

    /// <summary>
    /// Gets all available audio input devices
    /// </summary>
    public static List<AudioInputDevice> GetAvailableDevices()
    {
        var devices = new List<AudioInputDevice>();
        
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var capabilities = WaveInEvent.GetCapabilities(i);
            devices.Add(new AudioInputDevice
            {
                DeviceNumber = i,
                Name = capabilities.ProductName,
                Channels = capabilities.Channels
            });
        }
        
        return devices;
    }

    /// <summary>
    /// Start recording from the specified device, or default device if deviceNumber is -1
    /// </summary>
    public void StartRecording(int deviceNumber = -1)
    {
        if (_isRecording) return;

        try
        {
            _recordingStream = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber >= 0 ? deviceNumber : 0, // Default to first device if invalid
                WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono for Whisper
            };

            _waveWriter = new WaveFileWriter(new IgnoreDisposeStream(_recordingStream), _waveIn.WaveFormat);

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
            var errorMessage = $"Error: {ex.Message}";
            RecordingStopped?.Invoke(this, errorMessage);
            RecordingError?.Invoke(this, errorMessage);
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
        if (e.Exception != null)
        {
            RecordingError?.Invoke(this, $"Recording error: {e.Exception.Message}");
        }

        _waveWriter?.Dispose();
        _waveWriter = null;

        _waveIn?.Dispose();
        _waveIn = null;

        _recordingStream?.Dispose();
        _recordingStream = null;

        RecordingStopped?.Invoke(this, "Recording stopped");
    }

    private DateTime _silenceStartTime;

    private async Task DetectSilenceAsync(CancellationToken ct)
    {
        // Wait a bit before starting silence detection to allow audio to start
        await Task.Delay(500, ct);
        
        while (!ct.IsCancellationRequested && _isRecording)
        {
            await Task.Delay(100, ct);

            // Only check for silence if we have audio data
            if (_audioSamples.Count > 0)
            {
                if (_lastAudioLevel < SilenceThreshold)
                {
                    if (!_isSilent)
                    {
                        _isSilent = true;
                        _silenceStartTime = DateTime.Now;
                    }
                    else if ((DateTime.Now - _silenceStartTime).TotalMilliseconds > SilenceDurationMs)
                    {
                        // Silence for more than specified duration - trigger transcription
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
    }

    public void Dispose()
    {
        StopRecording();
        _silenceDetectionCts?.Dispose();
        _waveWriter?.Dispose();
        _waveIn?.Dispose();
        _recordingStream?.Dispose();
    }
}

/// <summary>
/// Represents an audio input device
/// </summary>
public class AudioInputDevice
{
    public int DeviceNumber { get; set; }
    public string Name { get; set; } = "";
    public int Channels { get; set; }
    
    public override string ToString() => Name;
}

/// <summary>
/// Helper stream that ignores Dispose to prevent premature closing
/// </summary>
internal class IgnoreDisposeStream : Stream
{
    private readonly Stream _inner;
    
    public IgnoreDisposeStream(Stream inner)
    {
        _inner = inner;
    }
    
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }
    
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    
    // Don't dispose the inner stream
    protected override void Dispose(bool disposing) { /* Do nothing */ }
}
