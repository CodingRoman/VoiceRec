# VoiceRec - Sprachaufnahme & Transkription

Eine C# Avalonia Desktop-Anwendung zur Sprachaufnahme mit lokaler Whisper-Transkription.

## Features

- 🎙️ **Sprachaufnahme** - Aufnahme über Mikrofon mit einem Klick
- 📊 **Waveform-Visualisierung** - Echtzeit-Anzeige der Stimme als Kurve
- 📝 **Automatische Transkription** - Lokale Transkription mit Whisper (wenn Sie aufhören zu reden)
- 🌐 **Übersetzung** - Übersetzung des Textes auf Englisch
- 🎨 **Modernes Design** - Transparentes, fancy UI
- 🔝 **Immer im Vordergrund** - Fenster bleibt sichtbar
- 🔄 **Auto-Updates** - VeloPack für automatische Updates

## Anforderungen

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime (enthalten)
- Mikrofon

## Installation

### Option 1: Direkte Verwendung

1. Kopieren Sie den gesamten `publish` Ordner auf Ihren Windows-PC
2. Stellen Sie sicher, dass das Whisper-Modell im Ordner `Models\ggml-small.bin` vorhanden ist
3. Führen Sie `VoiceRec.exe` aus

### Option 2: VeloPack Installer

Erstellen Sie einen Installer mit:
```bash
vpk pack -u VoiceRec -v 1.0.0 -p . --mainExe VoiceRec.exe
```

## Whisper-Modell

Das Whisper-Modell `ggml-small.bin` (~465 MB) ist erforderlich. Es sollte im `Models`-Ordner neben der EXE-Datei liegen.

### Download

Laden Sie das Modell herunter von:
- https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin

Oder benennen Sie es nach dem Download in `ggml-small.bin` um und legen Sie es in den `Models`-Ordner.

## Verwendung

1. Starten Sie die Anwendung
2. Klicken Sie auf **Record** um die Aufnahme zu starten
3. Sprechen Sie in das Mikrofon
4. Die Waveform zeigt Ihre Stimme in Echtzeit
5. Wenn Sie aufhören zu sprechen (2 Sekunden Stille), startet die Transkription automatisch
6. Klicken Sie auf **Translate** um den Text ins Englische zu übersetzen
7. Klicken Sie auf **Copy** um den Text in die Zwischenablage zu kopieren

## Tastenkürzel

- **ESC** - Minimieren
- **Mausklick** - Fokus auf Fenster

## Fenster-Verhalten

- Wenn das Fenster den Fokus verliert, wird es leicht transparent (85% Opazität)
- Wenn das Fenster wieder fokussiert wird, ist es vollständig sichtbar (100% Opazität)
- Das Fenster bleibt immer im Vordergrund

## Technologie-Stack

- **UI Framework**: Avalonia 11.2.1
- **MVVM**: CommunityToolkit.Mvvm
- **Audio**: NAudio 2.2.1
- **Transkription**: Whisper.net 1.2.0
- **Charts**: LiveChartsCore.SkiaSharpView.Avalonia
- **Auto-Updates**: Velopack

## Projektstruktur

```
VoiceRec/
├── Services/
│   ├── AudioRecordingService.cs   # Mikrofon-Aufnahme
│   ├── WhisperService.cs          # Lokale Transkription
│   └── TranslationService.cs      # Übersetzung
├── ViewModels/
│   └── MainViewModel.cs           # Haupt-ViewModel
├── Views/
│   ├── MainWindow.axaml           # UI-XAML
│   └── MainWindow.axaml.cs        # Code-Behind
├── Converters/
│   └── Converters.cs              # XAML-Konverter
└── App.axaml                      # App-Konfiguration
```

## Entwicklung

### Build

```bash
dotnet build
```

### Veröffentlichung

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

## Lizenz

MIT License
