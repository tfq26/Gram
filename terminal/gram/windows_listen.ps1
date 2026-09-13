try {
    Add-Type -AssemblyName System.Speech
    $recognizer = New-Object System.Speech.Recognition.SpeechRecognitionEngine
    $recognizer.SetInputToDefaultAudioDevice()
    $recognizer.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar))
    $result = $recognizer.Recognize([TimeSpan]::FromSeconds(12))
    if ($null -ne $result) {
        [PSCustomObject]@{ text = $result.Text; confidence = $result.Confidence } | ConvertTo-Json -Compress
    }
    $recognizer.Dispose()
} catch {
    exit 1
}
