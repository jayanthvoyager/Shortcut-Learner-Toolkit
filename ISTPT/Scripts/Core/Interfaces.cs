using System;

namespace ISTPT.Core
{
    /// <summary>
    /// Contract for a service that monitors user input and detects improvement opportunities.
    /// </summary>
    public interface IInputMonitor
    {
        event Action<string, string> OnInefficientActionDetected; // CommandName, Reason
        void StartMonitoring();
        void StopMonitoring();
    }

    /// <summary>
    /// Contract for the UI system that displays suggestions to the user.
    /// </summary>
    public interface ISuggestionSystem
    {
        void ShowSuggestion(string title, string shortcut, string timeSaved);
        void Snooze(TimeSpan duration);
    }

    /// <summary>
    /// Contract for tracking long-term usage statistics.
    /// </summary>
    public interface IAnalyticsService
    {
        void TrackManualAction(string commandId, double executionTime);
        void TrackShortcutAction(string commandId);
        double GetTotalWastedTime();
    }
}
