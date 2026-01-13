# Shortcut Learner Toolkit for Unity

A smart Unity Editor plugin designed to help developers learn shortcuts naturally while they work.

[![Watch the Demo](https://img.youtube.com/vi/lj33fRLIZ9A/0.jpg)](https://www.youtube.com/watch?v=lj33fRLIZ9A)
> *Click the image above to watch the demo video explaining how it works!*

## How It Works

The toolkit uses a dual-approach to help you master Unity shortcuts:

### 1. Reactive Suggestions (The "Nudge")
The plugin silently monitors your interactions with the Unity Editor. If it detects that you performed an action via the UI (like clicking a menu item or using a toolbar button) that could have been done faster with a shortcut, it immediately triggers a **Shortcut Opportunity** overlay.
*   **Example**: You click the "Move Tool" icon. The overlay pops up: *"Shortcut Opportunity: Press W"*.

### 2. Proactive Tips (The "Teacher")
When you aren't actively triggering inefficiencies, the overlay cycles through a curated list of useful Unity shortcuts. This "passive learning" mode helps you discover commands you didn't even know existed.
*   **Feature**: You can toggle the loop on/off or change the interval (e.g., every 20 seconds) directly from the overlay.

## How It Is Implemented

The project is built with a focus on clean architecture and non-intrusive UI.

### Core Architecture
*   **`IInputMonitor`**: The core interface that defines how user inputs are tracked.
*   **`InputMonitorEngine`**: The concrete implementation that hooks into Unity's Editor events. It listens for specific command executions and compares them against known shortcut bindings.
*   **`ServiceLocator`**: A lightweight pattern used to decouple the monitoring logic from the UI presentation.

### User Interface (UI Toolkit)
The overlay is built using Unity's modern **UI Toolkit** (USS & UXML) for a responsive and lightweight experience.
*   **`ShortcutSuggestionOverlay.cs`**: The main driver for the UI. It handles the display logic, the timer for the tip cycle, and the "Smart Lookup" feature that fetches descriptions for commands.
*   **`SuggestionOverlay.uxml`**: Defines the structure of the overlay (Toolbar, Labels, Buttons).
*   **`SuggestionOverlay.uss`**: Handles the styling, ensuring the overlay is compact (450px width), dark-themed, and fits seamlessly into the Scene View.

## Features
- **Smart Lookup**: Automatically generates human-readable descriptions for commands (e.g., "Window/General/Hierarchy" -> "Opens Hierarchy window").
- **Google Integration**: One-click "Google This" button to instantly search for more info on a specific shortcut.
- **Snooze**: Temporarily silence suggestions for 1 hour if you need deep focus.
- **Compact Design**: Minimal screen real estate usage with a collapsible/clean layout.
