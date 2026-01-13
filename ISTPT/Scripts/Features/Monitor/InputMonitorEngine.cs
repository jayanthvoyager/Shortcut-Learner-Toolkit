using System;
using System.Linq;
using ISTPT.Core;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace ISTPT.Features.Monitor
{
    [InitializeOnLoad]
    public class InputMonitorEngine : IInputMonitor
    {
        public event Action<string, string> OnInefficientActionDetected;

        private double _lastKeyTime;
        private const double Threshold = 0.1f; // 100ms

        // Static constructor for auto-initialization on load
        static InputMonitorEngine()
        {
            // Ensure we register the service as early as possible
            var instance = new InputMonitorEngine();
            ServiceLocator.Instance.Register<IInputMonitor>(instance);
            instance.StartMonitoring();
        }

        public void StartMonitoring()
        {
            // Re-enable the update loop for tool detection
            EditorApplication.update += OnEditorUpdate;
            
            // Hook into SceneView to detect command events (menu actions, shortcuts)
            SceneView.duringSceneGui += OnSceneGUI;
            
            // Hook into Hierarchy and Project windows as they often receive the command events
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
            EditorApplication.projectWindowItemOnGUI += OnProjectGUI;
            
            // Hook into Undo system to catch Context Menu actions (which don't always fire ExecuteCommand)
            Undo.postprocessModifications += OnUndoPostProcess; // (Renamed from OnPostProcessModifications for clarity if I could, but keeping consistent)
            
            // Hook into Play Mode changes
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
        }

        public void StopMonitoring()
        {
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            EditorApplication.projectWindowItemOnGUI -= OnProjectGUI;
            // Undo.postprocessModifications -= OnPostProcessModifications; // Not using this anymore, removed in prev step actually?
            // Actually I removed the method body but maybe left the subscription line? Let's be safe.
            Undo.postprocessModifications -= OnUndoPostProcess;
            
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged -= OnPauseStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Only care about ENTERING play mode via efficient means
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                CheckInefficiency("Play");
            }
        }

        private void OnPauseStateChanged(PauseState state)
        {
             if (state == PauseState.Paused)
             {
                 CheckInefficiency("Pause");
             }
        }

        private void CheckInefficiency(string commandName)
        {
            double timeSinceKey = EditorApplication.timeSinceStartup - _lastKeyTime;
            
            // If action happened > 0.1s after last key, it was likely clicked
            if (timeSinceKey > Threshold)
            {
                string shortcut = FindShortcutIdForCommand(commandName);
                if (!string.IsNullOrEmpty(shortcut))
                {
                    NotifyInefficiency($"Used '{commandName}' via toolbar", shortcut);
                }
            }
        }

        private UndoPropertyModification[] OnUndoPostProcess(UndoPropertyModification[] modifications)
        {
            // This method is called whenever an Undo grouping is finished.
            // We can check the name of the Undo group to guess the command.
            
            string currentUndoName = Undo.GetCurrentGroupName();
            double timeSinceKey = EditorApplication.timeSinceStartup - _lastKeyTime;

            // If an action happened via mouse (Context Menu or Main Menu)
            if (timeSinceKey > Threshold && !string.IsNullOrEmpty(currentUndoName))
            {
                 // Filter out selection changes or trivial stuff
                 if (currentUndoName == "Selection Change") return modifications;

                 Debug.Log($"[ISTPT Undo] Undo Action: '{currentUndoName}'");
                 
                 // Try to map Undo name to a Shortcut
                 // "Duplicate" -> "Duplicate"
                 // "Paste" -> "Paste"
                 // "Delete" -> "Delete"
                 
                 string shortcut = FindShortcutIdForCommand(currentUndoName);
                 if (!string.IsNullOrEmpty(shortcut))
                 {
                     NotifyInefficiency($"Used '{currentUndoName}' via menu", shortcut);
                 }
            }
            
            return modifications;
        }

        private void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            HandleGlobalEvent(Event.current);
        }

        private void OnProjectGUI(string guid, Rect selectionRect)
        {
            HandleGlobalEvent(Event.current);
        }

        private void HandleGlobalEvent(Event e)
        {
             if (e == null) return;
             
             // Detect ExecuteCommand and ValidateCommand events
            if (e.type == EventType.ExecuteCommand || e.type == EventType.ValidateCommand)
            {
                string commandName = e.commandName;
                double timeSinceKey = EditorApplication.timeSinceStartup - _lastKeyTime;
                
                // Debug.Log($"[ISTPT Global] Command: '{commandName}', Type: {e.type}");

                // If command executed more than 100ms after last key, it was likely a menu click
                if (e.type == EventType.ExecuteCommand && !string.IsNullOrEmpty(commandName))
                {
                    // Record history for cross-referencing with Undo system
                    _lastCommandName = NormalizeCommandName(commandName) ?? commandName;
                    _lastCommandTime = EditorApplication.timeSinceStartup;
                    
                    if (timeSinceKey > Threshold)
                    {
                        // Filter out common noise
                        if (commandName == "Find" || commandName == "SelectAll") return;

                        string shortcut = FindShortcutIdForCommand(commandName);
                        if (!string.IsNullOrEmpty(shortcut))
                        {
                            NotifyInefficiency($"Used '{commandName}' via menu", shortcut);
                        }
                    }
                }
            }
            
            if (e.isKey && (e.type == EventType.KeyDown || e.type == EventType.KeyUp))
            {
                _lastKeyTime = EditorApplication.timeSinceStartup;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;
            
            // Detect ExecuteCommand and ValidateCommand events
            if (e.type == EventType.ExecuteCommand || e.type == EventType.ValidateCommand)
            {
                string commandName = e.commandName;
                double timeSinceKey = EditorApplication.timeSinceStartup - _lastKeyTime;
                
                Debug.Log($"[ISTPT] Command detected: '{commandName}', Type: {e.type}, TimeSinceKey: {timeSinceKey:F3}s");
                
                // If command executed more than 100ms after last key, it was likely a menu click
                if (e.type == EventType.ExecuteCommand && timeSinceKey > Threshold && !string.IsNullOrEmpty(commandName))
                {
                    string shortcut = FindShortcutIdForCommand(commandName);
                    Debug.Log($"[ISTPT] Searching shortcut for '{commandName}': {(string.IsNullOrEmpty(shortcut) ? "NOT FOUND" : shortcut)}");
                    
                    if (!string.IsNullOrEmpty(shortcut))
                    {
                        NotifyInefficiency($"Used '{commandName}' via menu", shortcut);
                    }
                }
            }
            
            // Track keyboard input to update _lastKeyTime
            if (e.isKey && (e.type == EventType.KeyDown || e.type == EventType.KeyUp))
            {
                _lastKeyTime = EditorApplication.timeSinceStartup;
            }
        }

        // GlobalEventHandler removed due to API availability issues in user's environment.

        [MenuItem("Tools/ISTPT/Debug Status")]
        public static void DebugStatus()
        {
            var instance = ServiceLocator.Instance.Get<IInputMonitor>() as InputMonitorEngine;
            if (instance == null)
            {
                Debug.LogError("[ISTPT] Monitor NOT registered in ServiceLocator. Attempting manual restart.");
                var newInstance = new InputMonitorEngine();
                ServiceLocator.Instance.Register<IInputMonitor>(newInstance);
                newInstance.StartMonitoring();
            }
            else
            {
                Debug.Log($"[ISTPT] System is Alive. Last Tool: {instance._lastTool}. Monitoring Active.");
            }
        }

        private Tool _lastTool = Tool.None;
        private string _lastUndoGroup = "";
        
        // Track the last executed command to distinguish keyboard VS menu actions
        private string _lastCommandName = "";
        private double _lastCommandTime = 0;

        private void OnEditorUpdate()
        {
            // 1. Detect Undo/Redo Actions
            string currentUndo = Undo.GetCurrentGroupName();
            if (currentUndo != _lastUndoGroup)
            {
                // Normalize the undo name to a command name
                string mappedCommand = NormalizeCommandName(currentUndo);
                
                // If we have a valid command from undo...
                if (!string.IsNullOrEmpty(mappedCommand) && currentUndo != "Selection Change")
                {
                     double timeNow = EditorApplication.timeSinceStartup;
                     
                     // CHECK: Did we recently see this command fired via keyboard/event system?
                     // If user pressed F2, 'Rename' command would have fired ~0.1s ago.
                     // If user used Menu, 'Rename' command might NOT have fired, or fired long ago.
                     
                     bool commandWasFired = (mappedCommand == _lastCommandName && (timeNow - _lastCommandTime) < 1.0f);
                     
                     // Special Case for Rename: It involves typing and pressing Enter.
                     // So we can't rely on "TimeSinceKey". We must rely on "Did the Rename Command fire?"
                     
                     if (!commandWasFired)
                     {
                         string shortcut = FindShortcutIdForCommand(currentUndo);
                         if (!string.IsNullOrEmpty(shortcut))
                         {
                             NotifyInefficiency($"Used '{currentUndo}' via menu", shortcut);
                         }
                     }
                }
                _lastUndoGroup = currentUndo;
            }

            // 2. Detect Tool Changes... (Same as before)
            if (Tools.current != _lastTool)
            {
                 // ... existing tool logic ... 
                 // (Need to keep existing logic here, but for brevity in replacement I'll copy it back in full in the next step or assume user context)
                 // actually I must provide full content for the block I'm replacing.
                 
                double timeSinceKey = EditorApplication.timeSinceStartup - _lastKeyTime;
                
                if (Tools.current != _lastTool)
                {
                    Debug.Log($"[ISTPT Debug] Tool changed to {Tools.current}.");
    
                    if (timeSinceKey > Threshold && _lastTool != Tool.None) 
                    {
                        string layoutShortcut = GetShortcutForTool(Tools.current);
                        if (!string.IsNullOrEmpty(layoutShortcut))
                        {
                            NotifyInefficiency($"Switched to {Tools.current} Tool", layoutShortcut);
                        }
                    }
                    _lastTool = Tools.current;
                }
            }
        }
        
        private string NormalizeCommandName(string undoName)
        {
            if (string.IsNullOrEmpty(undoName)) return null;
            
            // Case-Insensitive Matching
            if (undoName.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0) return "Delete";
            if (undoName.IndexOf("SoftDelete", StringComparison.OrdinalIgnoreCase) >= 0) return "Delete"; // Handle internal command name
            
            if (undoName.IndexOf("Paste", StringComparison.OrdinalIgnoreCase) >= 0) return "Paste";
            if (undoName.IndexOf("Duplicate", StringComparison.OrdinalIgnoreCase) >= 0) return "Duplicate";
            if (undoName.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0) return "Copy";
            if (undoName.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0) return "Cut";
            if (undoName.IndexOf("Rename", StringComparison.OrdinalIgnoreCase) >= 0) return "Rename";
            
            // Expanded coverage
            if (undoName.IndexOf("Align", StringComparison.OrdinalIgnoreCase) >= 0) return "Align";
            if (undoName.IndexOf("Snap", StringComparison.OrdinalIgnoreCase) >= 0) return "Snap";
            if (undoName.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0) return "Play";
            if (undoName.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0) return "Pause";
            if (undoName.IndexOf("Step", StringComparison.OrdinalIgnoreCase) >= 0) return "Step";
            if (undoName.IndexOf("Selection", StringComparison.OrdinalIgnoreCase) >= 0) return "Select"; 
            if (undoName.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0) return "Grid";
            if (undoName.IndexOf("Layout", StringComparison.OrdinalIgnoreCase) >= 0) return "Layout";
            if (undoName.IndexOf("Inspector", StringComparison.OrdinalIgnoreCase) >= 0) return "Inspector";
            
            return null;
        }

        private void NotifyInefficiency(string commandName, string shortcut)
        {
            int subscriberCount = OnInefficientActionDetected?.GetInvocationList().Length ?? 0;
            // Debug.Log($"[ISTPT] Inefficient Action Detected: {commandName}. Shortcut: {shortcut}. Subscribers: {subscriberCount}");
            
            OnInefficientActionDetected?.Invoke(commandName, shortcut);
            
            if (subscriberCount == 0)
            {
                Debug.LogWarning("[ISTPT] No UI subscribers found. Please ensure the 'Shortcut Suggestions' Overlay is enabled in the Scene View Overlay Menu.");
            }
        }

        private string GetShortcutForTool(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return "W";
                case Tool.Rotate: return "E";
                case Tool.Scale: return "R";
                case Tool.Rect: return "T";
                case Tool.Transform: return "Y";
                default: return null;
            }
        }

        private string FindShortcutIdForCommand(string commandName)
        {
            // Normalize verbose Undo names to standard Command IDs
            string searchCmd = commandName;
            
            if (commandName.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Delete";
            else if (commandName.IndexOf("SoftDelete", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Delete";
            else if (commandName.IndexOf("Paste", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Paste";
            else if (commandName.IndexOf("Duplicate", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Duplicate";
            else if (commandName.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Copy";
            else if (commandName.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Cut";
            else if (commandName.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "FrameSelected";
            else if (commandName.IndexOf("Rename", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Rename";
            else if (commandName.IndexOf("Align", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Align";
            else if (commandName.IndexOf("Snap", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Snap";
            else if (commandName.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Play";
            else if (commandName.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Pause";
            else if (commandName.IndexOf("Step", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Step";
            else if (commandName.IndexOf("Select", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Select";
            else if (commandName.IndexOf("Inspector", StringComparison.OrdinalIgnoreCase) >= 0) searchCmd = "Inspector";

            // Unity's ShortcutManager allows querying shortcuts.
            foreach (var profileId in ShortcutManager.instance.GetAvailableShortcutIds())
            {
                // Exact Match
                if (string.Equals(profileId, searchCmd, StringComparison.OrdinalIgnoreCase))
                {
                   var binding = ShortcutManager.instance.GetShortcutBinding(profileId);
                   if (binding.keyCombinationSequence.Any()) return binding.ToString();
                }

                // Suffix Match
                if (profileId.EndsWith("/" + searchCmd, StringComparison.OrdinalIgnoreCase))
                {
                   var binding = ShortcutManager.instance.GetShortcutBinding(profileId);
                   if (binding.keyCombinationSequence.Any()) return binding.ToString();
                }
            }
            
            // Fallback
             switch (searchCmd)
            {
                case "Delete": return "Delete"; 
                case "Duplicate": return "Ctrl+D";
                case "Copy": return "Ctrl+C";
                case "Paste": return "Ctrl+V";
                case "Cut": return "Ctrl+X";
                case "Rename": return "F2";
                case "Inspector": return "Ctrl+3";
            }

            return null;
        }


    }
}
