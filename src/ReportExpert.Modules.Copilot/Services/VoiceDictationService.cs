using System.Globalization;
using System.Speech.Recognition;

namespace ReportExpert.Modules.Copilot.Services;

/// <summary>
/// English dictation for the Rex composer, on the Windows speech recognizer (offline, no key).
/// </summary>
/// <remarks>
/// Recognised phrases are raised one at a time so the view model can append them to the input box,
/// where the user reviews and edits the text before sending it. Events arrive on a background thread.
/// </remarks>
public sealed class VoiceDictationService : IDisposable
{
    private SpeechRecognitionEngine? _engine;

    /// <summary>A finished phrase.</summary>
    public event Action<string>? PhraseRecognized;

    /// <summary>The recognizer's current guess at the phrase being spoken.</summary>
    public event Action<string>? Hypothesis;

    /// <summary>Listening ended, by <see cref="Stop"/> or because the microphone went away.</summary>
    public event Action<string?>? Stopped;

    /// <summary>Whether the microphone is open.</summary>
    public bool IsListening => _engine is not null;

    /// <summary>
    /// Opens the default microphone and starts dictation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No English recognizer is installed or no microphone is available; the message says what to do.
    /// </exception>
    public void Start()
    {
        if (_engine is not null)
            return;

        RecognizerInfo? recognizer;
        try
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            recognizer = installed.FirstOrDefault(r => r.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en");
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or TypeInitializationException)
        {
            throw new InvalidOperationException("Voice input needs the Windows speech recognizer, which is not available here.");
        }

        if (recognizer is null)
        {
            throw new InvalidOperationException(
                "English speech recognition is not installed. In Windows Settings › Time & language › Language & region, " +
                "add English (United States) with the Speech option, then try again.");
        }

        var engine = new SpeechRecognitionEngine(recognizer);
        try
        {
            engine.LoadGrammar(new DictationGrammar { Name = "Dictation" });
            engine.SetInputToDefaultAudioDevice();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or InvalidCastException)
        {
            engine.Dispose();
            throw new InvalidOperationException("No microphone was found. Connect one (or allow microphone access in Windows privacy settings) and try again.");
        }

        // A pause ends a phrase; the user keeps talking and the next phrase follows.
        engine.EndSilenceTimeout = TimeSpan.FromSeconds(0.6);
        engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1);

        engine.SpeechRecognized += (_, e) =>
        {
            if (e.Result is { Text.Length: > 0, Confidence: >= 0.2f })
                PhraseRecognized?.Invoke(e.Result.Text);
        };
        engine.SpeechHypothesized += (_, e) => Hypothesis?.Invoke(e.Result.Text);
        engine.RecognizeCompleted += (_, e) =>
        {
            string? problem = e.Error is not null ? "Voice input stopped: " + e.Error.Message : null;
            Release();
            Stopped?.Invoke(problem);
        };

        _engine = engine;
        engine.RecognizeAsync(RecognizeMode.Multiple);
    }

    /// <summary>Stops listening. Any phrase in progress is finished and raised first.</summary>
    public void Stop()
    {
        if (_engine is { } engine)
        {
            try
            {
                engine.RecognizeAsyncStop();
            }
            catch (InvalidOperationException)
            {
                Release();
                Stopped?.Invoke(null);
            }
        }
    }

    public void Dispose()
    {
        if (_engine is { } engine)
        {
            try
            {
                engine.RecognizeAsyncCancel();
            }
            catch (InvalidOperationException)
            {
                // Already stopped.
            }

            Release();
        }
    }

    private void Release()
    {
        var engine = _engine;
        _engine = null;

        // Released off the recognizer's own callback thread, which it may still be using.
        if (engine is not null)
            _ = Task.Run(engine.Dispose);
    }

    /// <summary>The culture dictation listens for, shown in the UI.</summary>
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");
}
