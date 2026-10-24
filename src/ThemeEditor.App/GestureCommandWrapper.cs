// --------------------------------------------------
// Umami 3DS Theme Editor - GestureCommandWrapper.cs
// --------------------------------------------------

using System.Windows.Input;
using Avalonia;
using Avalonia.Input;

namespace ThemeEditor.App
{
    public class GestureCommandWrapper
    {
        public ICommand Command { get; }
        public KeyGesture Gesture { get; }
        public string GestureText => Gesture.ToString();

        public GestureCommandWrapper(ICommand command, KeyGesture gesture)
        {
            Command = command;
            Gesture = gesture;
        }

        /// <summary>The primary shortcut modifier: Ctrl on Windows/Linux, Cmd on macOS.</summary>
        public static KeyModifiers CommandModifier
        {
            get
            {
                var cfg = Application.Current?.PlatformSettings?.HotkeyConfiguration;
                return cfg?.CommandModifiers ?? KeyModifiers.Control;
            }
        }

        public static GestureCommandWrapper Create(ICommand command, Key key, bool shift = false)
        {
            var mod = CommandModifier | (shift ? KeyModifiers.Shift : KeyModifiers.None);
            return new GestureCommandWrapper(command, new KeyGesture(key, mod));
        }
    }
}
