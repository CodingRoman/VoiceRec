using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoiceRec.Services;

public class TranslationService : IDisposable
{
    private readonly string _ollamaHost;
    private bool _isOllamaRunning;
    private bool _disposed;

    public bool IsInitialized => _isOllamaRunning;
    public string Status => _isOllamaRunning ? "Ollama bereit" : "Ollama nicht verfügbar";

    public TranslationService(string ollamaHost = "http://localhost:11434")
    {
        _ollamaHost = ollamaHost;
    }

    /// <summary>
    /// Prüft ob Ollama läuft und verfügbar ist
    /// </summary>
    public async Task<bool> CheckOllamaAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var response = await httpClient.GetAsync($"{_ollamaHost}/api/tags");
            _isOllamaRunning = response.IsSuccessStatusCode;
            
            return _isOllamaRunning;
        }
        catch
        {
            _isOllamaRunning = false;
            return false;
        }
    }

    /// <summary>
    /// Übersetzt Text ins Englische mit Ollama (lokal)
    /// </summary>
    public async Task<string> TranslateToEnglishAsync(string germanText)
    {
        if (string.IsNullOrWhiteSpace(germanText))
            return string.Empty;

        // First check if Ollama is running
        if (!await CheckOllamaAsync())
        {
            return GetOfflineTranslation(germanText);
        }

        try
        {
            // Use Ollama API for translation
            var request = new
            {
                model = "llama3.2", // or any available model
                prompt = $"Translate the following German text to English. Only respond with the translation, nothing else:\n\n{germanText}",
                stream = false
            };

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(2);

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{_ollamaHost}/api/generate", content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson);
                
                if (result != null && !string.IsNullOrEmpty(result.response))
                {
                    return result.response.Trim();
                }
            }

            // Fallback to offline translation
            return GetOfflineTranslation(germanText);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ollama translation error: {ex.Message}");
            return GetOfflineTranslation(germanText);
        }
    }

    /// <summary>
    /// Startet Ollama wenn es installiert aber nicht gestartet ist
    /// </summary>
    public async Task<bool> StartOllamaAsync()
    {
        try
        {
            // Versuche Ollama zu starten
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            
            // Warten bis Ollama bereit ist
            await Task.Delay(3000);
            
            return await CheckOllamaAsync();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Listet verfügbare Ollama-Modelle auf
    /// </summary>
    public async Task<string[]> GetAvailableModelsAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await httpClient.GetAsync($"{_ollamaHost}/api/tags");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaTagsResponse>(json);
                
                if (result?.models != null)
                {
                    var names = new string[result.models.Length];
                    for (int i = 0; i < result.models.Length; i++)
                    {
                        names[i] = result.models[i].name;
                    }
                    return names;
                }
            }
        }
        catch { }
        
        return Array.Empty<string>();
    }

    /// <summary>
    /// Lädt ein Ollama-Modell herunter wenn nicht vorhanden
    /// </summary>
    public async Task<bool> PullModelAsync(string modelName)
    {
        try
        {
            var request = new { name = modelName };
            var json = JsonSerializer.Serialize(request);
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for download
            
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync($"{_ollamaHost}/api/pull", content);
            
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Einfache Offline-Übersetzung ohne externe Dienste
    /// </summary>
    private string GetOfflineTranslation(string text)
    {
        var translations = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Greetings
            { "hallo", "hello" },
            { "guten morgen", "good morning" },
            { "guten tag", "good day" },
            { "guten abend", "good evening" },
            { "gute nacht", "good night" },
            { "auf wiedersehen", "goodbye" },
            { "bis später", "see you later" },
            { "tschüss", "bye" },
            
            // Politeness
            { "danke", "thank you" },
            { "danke schön", "thank you very much" },
            { "vielen dank", "many thanks" },
            { "bitte", "please" },
            { "bitte schön", "you're welcome" },
            { "entschuldigung", "excuse me" },
            { "sorry", "sorry" },
            
            // Common
            { "ja", "yes" },
            { "nein", "no" },
            { "vielleicht", "maybe" },
            { "natürlich", "of course" },
            
            // Questions
            { "wie geht es dir", "how are you" },
            { "wie geht es ihnen", "how are you" },
            { "was machst du", "what are you doing" },
            { "was ist das", "what is this" },
            { "wo bist du", "where are you" },
            { "wann", "when" },
            { "warum", "why" },
            { "wie", "how" },
            
            // Statements
            { "mir geht es gut", "i'm fine" },
            { "es geht mir gut", "i'm fine" },
            { "ich bin müde", "i am tired" },
            { "ich bin hungrig", "i am hungry" },
            { "ich habe hunger", "i am hungry" },
            { "ich bin durstig", "i am thirsty" },
            { "ich habe durst", "i am thirsty" },
            { "ich verstehe", "i understand" },
            { "ich verstehe nicht", "i don't understand" },
            { "das ist gut", "that is good" },
            { "das ist schlecht", "that is bad" },
            { "ich weiß nicht", "i don't know" },
            { "keine ahnung", "no idea" },
            
            // Verbs
            { "sprechen", "to speak" },
            { "hören", "to hear" },
            { "sehen", "to see" },
            { "gehen", "to go" },
            { "kommen", "to come" },
            { "essen", "to eat" },
            { "trinken", "to drink" },
            { "schlafen", "to sleep" },
            { "arbeiten", "to work" },
            
            // Objects
            { "wasser", "water" },
            { "kaffee", "coffee" },
            { "tee", "tea" },
            { "brot", "bread" },
            { "essen", "food" },
            
            // Time
            { "jetzt", "now" },
            { "später", "later" },
            { "früh", "early" },
            { "spät", "late" },
            { "heute", "today" },
            { "morgen", "tomorrow" },
            { "gestern", "yesterday" }
        };

        var lowerText = text.ToLower().Trim();
        
        foreach (var kvp in translations)
        {
            if (lowerText.Contains(kvp.Key.ToLower()))
            {
                return $"[Simple translation: {kvp.Value}]";
            }
        }

        return "[Ollama not running. Please install and start Ollama for translations.]";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

// JSON Response classes for Ollama API
public class OllamaResponse
{
    public string? response { get; set; }
    public bool done { get; set; }
}

public class OllamaTagsResponse
{
    public OllamaModel[]? models { get; set; }
}

public class OllamaModel
{
    public string name { get; set; } = "";
    public string modified_at { get; set; } = "";
    public long size { get; set; }
}
