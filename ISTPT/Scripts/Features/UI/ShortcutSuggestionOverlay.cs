using System;
using System.Collections.Generic;
using System.Linq;
using ISTPT.Core;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace ISTPT.Features.UI
{
    [Overlay(typeof(SceneView), "Shortcut Suggestions", defaultDisplay: true)]
    public class ShortcutSuggestionOverlay : Overlay, ISuggestionSystem
    {
        // STATIC: Track the currently active instance to prevent ghost subscriptions
        private static ShortcutSuggestionOverlay _activeInstance;
        
        // STATIC: Cache the last message so recreated overlays can restore state
        private static string _lastTitle = "Waiting...";
        private static string _lastShortcut = "--";
        
        private VisualElement _root;
        private VisualElement _container;
        private Label _lblActionName;
        private Label _lblShortcut;
        // private Label _lblTimeSaved; // Removed
        private Button _btnSnooze;
        private Button _btnClose;
        
        // Track the active timer to cancel it if a new message arrives
        private IVisualElementScheduledItem _hideTimer;
        private IVisualElementScheduledItem _tipTimer;
        
        // Dynamic list, populated at runtime from Unity's ShortcutManager
        private List<(string title, string shortcut, string desc, string context)> _tips = new List<(string, string, string, string)>();

        // Curated dictionary of explanations for common commands
        private readonly Dictionary<string, string> _commandDescriptions = new Dictionary<string, string>
        {
            // View / Navigation
            { "Frame Selected", "Centers camera on object" },
            { "Orbit Camera", "Rotates view around object" },
            { "Pan Camera", "Moves view laterally" },
            { "Zoom Camera", "Moves camera closer/further" },
            { "Fly Mode", "First-person camera movement" },
            { "Maximize Window", "Toggles full-screen view" },
            
            // Tools
            { "Move", "Position objects (W)" },
            { "Rotate", "Rotate objects (E)" },
            { "Scale", "Resize objects (R)" },
            { "RectT", "UI Resizing Tool (T)" },
            { "Transform", "Combined Tool (Y)" },
            { "Toggle Global/Local", "Switches rotation axis" },
            { "Toggle Pivot/Center", "Switches handle position" },
            
            // Edit
            { "Play", "Starts Play Mode" },
            { "Pause", "Pauses Play Mode" },
            { "Step", "Advance one frame" },
            { "Duplicate", "Clones selection" },
            { "Delete", "Removes selection" },
            { "Rename", "Renames selection" },
            { "Copy", "Copies to clipboard" },
            { "Paste", "Pastes from clipboard" },
            { "Cut", "Moves to clipboard" },
            
            // Windows
            { "Inspector", "Shows object properties" },
            { "Console", "Shows debug logs" },
            { "Hierarchy", "Shows scene structure" },
            { "Project", "Shows asset files" },
            { "Scene", "Shows 3D View" },
            { "Game", "Shows Game View" },
            
            // Selection
            { "SelectAll", "Selects everything" },
            { "InvertSelection", "Selects unselected items" },
            
            // Layout
            { "Align", "Aligns objects" },
            { "Snap", "Snaps to grid/vertex" }
        };

        public override VisualElement CreatePanelContent()
        {
            // CRITICAL: Ensure any old timers from previous runs are cleared!
            StopTipCycle();
            
            // ... (keep start logic same) ...
            // (I need to include context enough to replace _tips definition and PopulateTips usage)
            
            // ... 
            
            // CRITICAL: Unsubscribe any previous "ghost" instance
            if (_activeInstance != null && _activeInstance != this)
            {
                var monitor = ServiceLocator.Instance.Get<IInputMonitor>();
                if (monitor != null)
                {
                    monitor.OnInefficientActionDetected -= _activeInstance.OnInefficiencyDetected;

                }
            }
            _activeInstance = this;
            
            _root = new VisualElement();
            _root.style.flexGrow = 1;
            _root.style.minWidth = 400;
            _root.style.minHeight = 100;
            
            var inputMonitor = ServiceLocator.Instance.Get<IInputMonitor>();
            if (inputMonitor != null)
            {
                inputMonitor.OnInefficientActionDetected -= OnInefficiencyDetected;
                inputMonitor.OnInefficientActionDetected += OnInefficiencyDetected;
            }
            ServiceLocator.Instance.Register<ISuggestionSystem>(this);

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ISTPT/UI/SuggestionOverlay.uxml");
            if (visualTree != null)
            {
                visualTree.CloneTree(_root);
                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/ISTPT/UI/SuggestionOverlay.uss");
                if (styleSheet != null) _root.styleSheets.Add(styleSheet);
            }
            else { _root.Add(new Label("Error loading UXML")); }

            _container = _root.Q<VisualElement>("container");
            _lblActionName = _root.Q<Label>("lbl-action-name");
            _lblShortcut = _root.Q<Label>("lbl-shortcut");
            _btnSnooze = _root.Q<Button>("btn-snooze");
            _btnClose = _root.Q<Button>("btn-close");
            
            PopulateTips();

            if (_lblActionName != null) _lblActionName.text = _lastTitle;
            if (_lblShortcut != null) _lblShortcut.text = _lastShortcut;

            if (_container != null && _lastTitle != "Waiting...")
            {
                _container.style.display = DisplayStyle.Flex;
                // Removed legacy auto-hide: Proactive tips should stay open.
            }

            if (_btnSnooze != null) _btnSnooze.clicked += () => Snooze(TimeSpan.FromHours(1));
            if (_btnClose != null) _btnClose.clicked += () => Snooze(TimeSpan.FromMinutes(1));
            
            var btnGoogle = _root.Q<Button>("btn-google-search");
            if (btnGoogle != null)
            {
                btnGoogle.clicked += () =>
                {
                    // Search format: Unity [Context] [Title]
                    // e.g. "Theory of Unity [Animation] Next Frame"
                    string query = $"Unity \"{_lastTitle}\" shortcut";
                    Application.OpenURL($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                };
            }

             // --- New Toolbar Controls ---
            var toggleLoop = _root.Q<Toggle>("toggle-loop");
            var fieldInterval = _root.Q<IntegerField>("field-interval");


            // Init values from current state
            // Immediate Updates with RegisterValueChangedCallback
            if (toggleLoop != null)
            {
                toggleLoop.value = _configLoopEnabled;
                toggleLoop.RegisterValueChangedCallback(evt => 
                {
                    _configLoopEnabled = evt.newValue;
                    RestartTipCycleWithNewConfig();
                    // If we just enabled it, show a tip immediately if there isn't one? 
                    // Actually RestartTipCycleWithNewConfig handles the starting.
                });
            }

            if (fieldInterval != null)
            {
                fieldInterval.value = _configIntervalSeconds;
                fieldInterval.RegisterValueChangedCallback(evt =>
                {
                    // Enforce minimum 2 seconds
                    int newValue = Mathf.Max(2, evt.newValue);
                    
                    // If value was clamped, update UI back (prevent invalid input)
                    if (newValue != evt.newValue) fieldInterval.value = newValue;

                    _configIntervalSeconds = newValue;
                    RestartTipCycleWithNewConfig();
                });
            }

            StartTipCycle();

            return _root;
        }

        // Configuration State
        private bool _configLoopEnabled = true;
        private int _configIntervalSeconds = 20;

        private void RestartTipCycleWithNewConfig()
        {
            StopTipCycle();
            if (_configLoopEnabled)
            {
                StartTipCycle();
            }
        }

        private void PopulateTips()
        {
            _tips.Clear();
            var shortcuts = ShortcutManager.instance.GetAvailableShortcutIds();
            foreach (var id in shortcuts)
            {
                var binding = ShortcutManager.instance.GetShortcutBinding(id);
                if (binding.keyCombinationSequence.Any())
                {
                    string title = id;
                    string context = "Global";
                    
                    int lastSlash = id.LastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash < id.Length - 1)
                    {
                        title = id.Substring(lastSlash + 1);
                        context = id.Substring(0, lastSlash).Replace("/", " > "); // "Window/General" -> "Window > General"
                    }
                    else
                    {
                        context = "Global";
                    }
                    
                    // Use smart lookup
                    string desc = GetSmartDescription(id, title);
                    
                    _tips.Add((title, binding.ToString(), desc, context));
                }
            }
            
            if (_tips.Count == 0) _tips.Add(("No active shortcuts", "Check Settings", "", "Error"));
            

        }

        private string GetSmartDescription(string fullId, string shortName)
        {
            // 1. Manual Dictionary (Highest Quality)
            // Try exact match
            if (_commandDescriptions.TryGetValue(shortName, out string manualDesc)) return manualDesc;
            
            // Try partial match (e.g. "Tools/Move" matching "Move")
            foreach (var kvp in _commandDescriptions)
            {
                if (shortName.Contains(kvp.Key)) return kvp.Value;
            }

            // 2. Heuristic Parsing (Good Quality)
            
            // "Window/General/Hierarchy" -> "Opens Hierarchy window"
            if (fullId.Contains("Window/"))
                return $"Opens {SplitCamelCase(shortName)} window";
                
            // "Component/Physics/Box Collider" -> "Adds Box Collider component"
            if (fullId.Contains("Component/"))
                return $"Adds {SplitCamelCase(shortName)} component";
                
            // "Assets/Create/Folder" -> "Creates new Folder"
            if (fullId.Contains("Create/"))
                return $"Creates new {SplitCamelCase(shortName)}";
                
            // "Edit/Grid and Snap/..."
            if (fullId.Contains("Snap"))
                return "Snapping tool command";

            // 3. Last Resort (Generic)
            // "Main Menu/File/Save" -> "Executes Save command"
            return $"Executes {SplitCamelCase(shortName)} command";
        }

        // Helper to make "FrameSelected" look like "Frame Selected"
        private string SplitCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return System.Text.RegularExpressions.Regex.Replace(text, "(\\B[A-Z])", " $1");
        }
        
        // ... (Cleanup methods same) ...

        private void ShowRandomTip()
        {
            if (_container == null) return;
            if (_hideTimer != null) return; // Don't overwrite warning

            var randomTip = _tips[UnityEngine.Random.Range(0, _tips.Count)];
            
            // Cache state for Search Button
            _lastTitle = randomTip.title;
            _lastShortcut = randomTip.shortcut;

            // Format: "Did you know?" -> "Orbit Camera"
            // Subtext: (Rotates view around object)
            
            _lblActionName.text = string.IsNullOrEmpty(randomTip.desc) 
                ? randomTip.title 
                : $"{randomTip.title}\n({randomTip.desc})"; // Append description
                
            _lblShortcut.text = randomTip.shortcut;
            
            // Avoid re-setting style if already Flex (prevents layout thrashing/black flicker)
            if (_container.style.display.value != DisplayStyle.Flex)
            {
                _container.style.display = DisplayStyle.Flex;
            }
            
            // Removed: _hideTimer settings. Proactive tips stay until next cycle or warning.
        }

        public override void OnWillBeDestroyed()
        {
            if (_activeInstance == this)
            {
                var monitor = ServiceLocator.Instance.Get<IInputMonitor>();
                if (monitor != null)
                {
                    monitor.OnInefficientActionDetected -= OnInefficiencyDetected;
                }
                _activeInstance = null;
                StopTipCycle();
            }
            base.OnWillBeDestroyed();
        }

        private void OnInefficiencyDetected(string command, string shortcut)
        {

             ShowSuggestion("Shortcut Available", $"{command}: {shortcut}", "2s");
        }

        private void StartTipCycle()
        {
            if (_tipTimer == null && _configLoopEnabled)
            {
                // Update tip every X seconds (converted to ms)
                long intervalMs = _configIntervalSeconds * 1000;
                _tipTimer = _root.schedule.Execute(ShowRandomTip).Every(intervalMs);
                
                // Note: We don't call ShowRandomTip() immediately here usually, 
                // unless it's the very first start.
            }
        }
        
        private void StopTipCycle()
        {
            if (_tipTimer != null)
            {
                _tipTimer.Pause();
                _tipTimer = null;
            }
        }



        // Snooze state
        private DateTime _snoozeUntil;

        public void ShowSuggestion(string title, string shortcut, string timeSaved)
        {
            if (_root == null) return;
            
            // Checking Snooze: If we are snoozed, ignore new warnings
            if (DateTime.Now < _snoozeUntil)
            {
                return;
            }
            
            // Stop tips temporarily so the user sees the warning without interruption
            StopTipCycle();
            
            // Query elements FRESH every time
            var container = _root.Q<VisualElement>("container");
            var lblActionName = _root.Q<Label>("lbl-action-name");
            var lblShortcut = _root.Q<Label>("lbl-shortcut");
            
            if (container == null || lblActionName == null || lblShortcut == null) return;
            
            _lastTitle = title;
            _lastShortcut = shortcut;
                
            lblActionName.text = title;
            lblShortcut.text = shortcut;
            // CRITICAL: Force Unity to actually REDRAW the damn thing
            container.MarkDirtyRepaint();
            
            // Cancel previous hide timer (for the warning itself)
            if (_hideTimer != null)
            {
                _hideTimer.Pause();
                _hideTimer = null;
            }
            
            // Auto hide Warning after 10 seconds
            _hideTimer = container.schedule.Execute(HideWarningAndResumeTips);
            _hideTimer.ExecuteLater(10000);
        }
        
        private void HideWarningAndResumeTips()
        {
            _hideTimer = null; // Clear flag
            StartTipCycle();   // Restart the cycle
        }

        public void Snooze(TimeSpan duration)
        {
            // Set suppression time
            _snoozeUntil = DateTime.Now.Add(duration);
            
            // 1. Force kill the warning timer if active
            if (_hideTimer != null)
            {
               _hideTimer.Pause();
               _hideTimer = null;
            }
            
            // 2. Force restart the tip cycle (Stop then Start ensures an immediate new tip)
            StopTipCycle();
            StartTipCycle();
        }
        
        private void Hide()
        {
            if (_container != null) _container.style.display = DisplayStyle.None;
        }
    }
}
