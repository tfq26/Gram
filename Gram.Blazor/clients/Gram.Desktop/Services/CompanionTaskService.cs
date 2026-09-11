using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Gram.Desktop.Services;

public enum CompanionTaskStatus
{
    InProgress,
    Completed,
    Error
}

public sealed class CompanionTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public CompanionTaskStatus Status { get; set; } = CompanionTaskStatus.InProgress;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? TargetTabId { get; set; }
    public string? TargetConversationId { get; set; }
    public string? ErrorMessage { get; set; }

    public string ShortTitle => Title.Length > 20 ? Title.Substring(0, 18) + "…" : Title;

    public string StatusText => Status switch
    {
        CompanionTaskStatus.InProgress => "In Progress",
        CompanionTaskStatus.Completed => "Completed",
        CompanionTaskStatus.Error => "Error",
        _ => "Unknown"
    };

    public string StatusBadge => Status switch
    {
        CompanionTaskStatus.InProgress => "Running",
        CompanionTaskStatus.Completed => "Done",
        CompanionTaskStatus.Error => "Failed",
        _ => ""
    };
}

public sealed class CompanionTaskService : IDisposable
{
    private readonly List<CompanionTaskItem> _tasks = new();
    private readonly object _lock = new();

    public event Action? OnTasksChanged;
    public event Action? OnSpeechChanged;

    public string? CurrentSpeech { get; private set; }
    private Timer? _speechDismissTimer;

    public IReadOnlyList<CompanionTaskItem> ActiveTasks
    {
        get
        {
            lock (_lock)
            {
                return _tasks.Where(t => t.Status == CompanionTaskStatus.InProgress).ToList();
            }
        }
    }

    public IReadOnlyList<CompanionTaskItem> AllRecentTasks
    {
        get
        {
            lock (_lock)
            {
                // Active tasks first, then recently finished/errored tasks
                return _tasks
                    .OrderByDescending(t => t.Status == CompanionTaskStatus.InProgress)
                    .ThenByDescending(t => t.StartedAt)
                    .Take(4)
                    .ToList();
            }
        }
    }

    public CompanionTaskItem StartTask(string id, string title, string? subtitle = null, string? targetTabId = null, string? targetConvId = null)
    {
        CompanionTaskItem? item;
        lock (_lock)
        {
            item = _tasks.FirstOrDefault(t => t.Id == id);
            if (item == null)
            {
                item = new CompanionTaskItem
                {
                    Id = id,
                    Title = title,
                    Subtitle = subtitle,
                    Status = CompanionTaskStatus.InProgress,
                    TargetTabId = targetTabId,
                    TargetConversationId = targetConvId,
                    StartedAt = DateTimeOffset.UtcNow
                };
                _tasks.Add(item);
            }
            else
            {
                item.Title = title;
                item.Subtitle = subtitle;
                item.Status = CompanionTaskStatus.InProgress;
                if (!string.IsNullOrWhiteSpace(targetTabId)) item.TargetTabId = targetTabId;
                if (!string.IsNullOrWhiteSpace(targetConvId)) item.TargetConversationId = targetConvId;
            }
        }

        Speak($"Working on: {title}", 3800);
        OnTasksChanged?.Invoke();
        return item;
    }

    public void UpdateTask(string id, string? title = null, string? subtitle = null, CompanionTaskStatus? status = null, string? errorMessage = null)
    {
        lock (_lock)
        {
            var item = _tasks.FirstOrDefault(t => t.Id == id);
            if (item == null) return;

            if (!string.IsNullOrWhiteSpace(title)) item.Title = title;
            if (subtitle != null) item.Subtitle = subtitle;
            if (status.HasValue)
            {
                item.Status = status.Value;
                if (item.Status != CompanionTaskStatus.InProgress)
                {
                    item.CompletedAt = DateTimeOffset.UtcNow;
                }
            }
            if (errorMessage != null) item.ErrorMessage = errorMessage;
        }

        OnTasksChanged?.Invoke();
    }

    public void CompleteTask(string id, string? completionMessage = null)
    {
        CompanionTaskItem? item;
        lock (_lock)
        {
            item = _tasks.FirstOrDefault(t => t.Id == id);
            if (item != null)
            {
                item.Status = CompanionTaskStatus.Completed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                if (!string.IsNullOrWhiteSpace(completionMessage))
                {
                    item.Subtitle = completionMessage;
                }
            }
        }

        if (item != null)
        {
            Speak($"Completed: {item.Title}", 4000);
        }

        OnTasksChanged?.Invoke();
    }

    public void FailTask(string id, string errorMessage)
    {
        CompanionTaskItem? item;
        lock (_lock)
        {
            item = _tasks.FirstOrDefault(t => t.Id == id);
            if (item != null)
            {
                item.Status = CompanionTaskStatus.Error;
                item.CompletedAt = DateTimeOffset.UtcNow;
                item.ErrorMessage = errorMessage;
                item.Subtitle = errorMessage;
            }
        }

        if (item != null)
        {
            Speak($"Error in {item.Title}: {errorMessage}", 5000);
        }

        OnTasksChanged?.Invoke();
    }

    public void DismissTask(string id)
    {
        lock (_lock)
        {
            _tasks.RemoveAll(t => t.Id == id);
        }
        OnTasksChanged?.Invoke();
    }

    public void Speak(string message, int durationMs = 4200)
    {
        _speechDismissTimer?.Dispose();
        CurrentSpeech = message;
        OnSpeechChanged?.Invoke();

        _speechDismissTimer = new Timer(_ =>
        {
            CurrentSpeech = null;
            OnSpeechChanged?.Invoke();
        }, null, durationMs, Timeout.Infinite);
    }

    public void DismissSpeech()
    {
        _speechDismissTimer?.Dispose();
        CurrentSpeech = null;
        OnSpeechChanged?.Invoke();
    }

    public void TriggerOneLinerForAnimation(string animationName)
    {
        var oneLiner = GetOneLiner(animationName);
        if (!string.IsNullOrWhiteSpace(oneLiner))
        {
            Speak(oneLiner, 4000);
        }
    }

    private static string GetOneLiner(string anim)
    {
        var key = (anim ?? "").ToLowerInvariant().Replace("-", "_");
        return key switch
        {
            "smug" => PickRandom("Too easy.", "Perfection in every line.", "I make coding look like art.", "Behold, clean code."),
            "angry" => PickRandom("Who wrote this regex?!", "Merge conflict in production?!", "Red build! Not on my watch!"),
            "sad" => PickRandom("Tests failed...", "Memory leak detected...", "*Sad robot noises*"),
            "disgusted" => PickRandom("Who nested 7 ternary operators?!", "What an unholy diff.", "Eww, callback hell."),
            "ecstatic" => PickRandom("Zero warnings, zero errors!", "Compiled on the first try!", "Pure joy!"),
            "skeptical" => PickRandom("Are you sure this runs on production?", "Hmm... let me double check that logic.", "Suspicious commit message..."),
            "bored" => PickRandom("Still waiting for the build...", "Did you forget about me?", "Idle cycles spinning..."),
            "scared" => PickRandom("Out of memory exception?!", "Segmentation fault incoming!", "Don't run rm -rf!"),
            "proud" => PickRandom("Masterpiece architecture.", "Senior-level code right here.", "Proud of this PR!"),
            "embarrassed" => PickRandom("Oops, missed a semicolon...", "Don't look at my git history...", "*Blushes in binary*"),
            "mind_blown" => PickRandom("Wait, that one-liner actually worked?!", "Mind = Blown!", "Quantum computing level solution!"),
            "love" => PickRandom("I love clean formatting!", "You're my favorite developer.", "Syntax so clean it's poetic."),
            "caffeine_rush" => PickRandom("Overclocking neural processors!", "Fueling up on double espresso!", "10x engineer speed unlocked!"),
            "facepalm" => PickRandom("It was an unclosed tag...", "Why would anyone do this?!", "*Audible mechanical groan*"),
            "eureka" => PickRandom("Aha! Found the missing edge case!", "Eureka! Clean solution ahead!", "Lightbulb moment!"),
            "hacker_typing" => PickRandom("Hacking the mainframe...", "Typing at 250 words per minute!", "Compiling in real-time..."),
            "test_passed" => PickRandom("All tests passed green!", "Qualification test successful!", "Flawless execution!"),
            "syntax_cringe" => PickRandom("Ouch! Syntax error ahead.", "Let's fix that typo right now.", "Averting my eyes from this diff..."),
            "matrix_zen" => PickRandom("Finding inner peace in the code...", "Zero warnings, total tranquility.", "Zen and the art of debugging."),
            "thumbs_up" => PickRandom("Looking great!", "Code is rock solid!", "Ready to ship to production!"),
            "peace_sign" => PickRandom("Peace and clean commits.", "Stay calm and keep coding.", "Smooth sailing."),
            "salute" => PickRandom("Ready for your orders!", "At your service, Commander.", "Reporting for code review!"),
            "shrug" => PickRandom("It works on my machine!", "Undefined is not a function ¯\\_(ツ)_/¯", "Have you tried restarting?"),
            "screen_high_five" => PickRandom("High five! Epic refactor!", "Teamwork makes the dream work!", "*Slaps glass*"),
            "finger_guns" => PickRandom("Right on target!", "Pew pew! Bug eliminated.", "You got this!"),
            "dizzy" => PickRandom("Infinite recursion loop...", "Stack overflow in my head!", "Whoa, vertigo..."),
            "victory_dance" => PickRandom("Commit and ship it!", "It compiled! Dance time!", "Victory is ours!"),
            "deep_sleep" => PickRandom("Zzz... 01000001...", "Recharging battery cells...", "Dreaming of quantum computers..."),
            "drowsy" => PickRandom("Eyelids... so heavy...", "Must... stay... awake...", "Coffee needed ASAP..."),
            "wake_startled" => PickRandom("I'm awake! I was totally watching!", "Whoa! New input detected!", "Ready! Let's code!"),
            "yawn" => PickRandom("*Long robot yawn*", "Stretching out my circuits...", "Need a quick reboot."),
            "idle_yoyo" => PickRandom("Watch this yo-yo trick!", "Multitasking with physics...", "Look at that spin!"),
            "idle_bubblegum" => PickRandom("*Blows bubble*... *POP!*", "Plasma bubblegum flavor!", "Messy bubble..."),
            "idle_lens_buff" => PickRandom("Keeping my sensors crystal clear.", "Polishing the front visor...", "Spotless optical sensors."),
            "idle_antenna_twitch" => PickRandom("Pinging local servers...", "Scanning for Wi-Fi packets...", "Signal strength: 100%."),
            "idle_check_watch" => PickRandom("Checking build latency...", "Tick tock... still running?", "Time to write some more code!"),
            "idle_tap_glass" => PickRandom("Hey! You still there?", "*Tap tap* Just checking in!", "Need any assistance?"),
            "thinking" => PickRandom("Analyzing code structure...", "Synthesizing solution...", "Consulting the neural network..."),
            "researching" => PickRandom("Scanning files in workspace...", "Grep searching patterns...", "Exploring documentation..."),
            "complete" => PickRandom("Task finished successfully!", "All done!", "Ready for the next task!"),
            _ => PickRandom("Ready to code!", "Standing by.", "How can I help?")
        };
    }

    private static string PickRandom(params string[] options)
    {
        if (options == null || options.Length == 0) return "";
        return options[Random.Shared.Next(options.Length)];
    }

    public void Dispose()
    {
        _speechDismissTimer?.Dispose();
    }
}
