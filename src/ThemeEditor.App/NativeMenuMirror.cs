// --------------------------------------------------
// Umami 3DS Theme Editor - NativeMenuMirror.cs
// --------------------------------------------------

using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Shows the window's menu in the macOS menu bar. The window menu stays the one source: each menu is filled from it
    ///     again whenever macOS is about to show it, so commands, ticks, hidden entries and the recent files are always
    ///     current, and choosing an entry clicks the window menu's item.
    /// </summary>
    internal static class NativeMenuMirror
    {
        /// <summary>Fills <paramref name="bar" /> (a new menu when null) with the menus of <paramref name="source" />; empties it when source is null.</summary>
        public static NativeMenu Build(Menu source, NativeMenu bar = null)
        {
            bar = bar ?? new NativeMenu();
            bar.Items.Clear();
            if (source == null)
                return bar;
            foreach (var top in source.Items.OfType<MenuItem>())
            {
                // The macOS menu bar only opens menus, so an entry that is a command by itself gets a menu holding it
                var holder = top.Items.Count > 0 ? top : null;
                var menu = new NativeMenu();
                menu.NeedsUpdate += (s, e) => Fill(menu, holder != null ? holder.Items.ToArray() : new object[] {top});
                Fill(menu, holder != null ? holder.Items.ToArray() : new object[] {top});
                bar.Items.Add(new NativeMenuItem(Header(top)) {Menu = menu});
            }
            return bar;
        }

        /// <summary>Replaces the entries of <paramref name="menu" /> with those of the window menu's <paramref name="items" />.</summary>
        internal static void Fill(NativeMenu menu, object[] items)
        {
            menu.Items.Clear();
            foreach (var entry in items)
            {
                if (entry is Separator)
                {
                    // No separator first, last or twice in a row (entries around it may be hidden)
                    if (menu.Items.Count > 0 && !(menu.Items[menu.Items.Count - 1] is NativeMenuItemSeparator))
                        menu.Items.Add(new NativeMenuItemSeparator());
                    continue;
                }
                if (!(entry is MenuItem item) || !item.IsVisible)
                    continue;
                menu.Items.Add(Mirror(item));
            }
            if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is NativeMenuItemSeparator)
                menu.Items.RemoveAt(menu.Items.Count - 1);
        }

        private static NativeMenuItem Mirror(MenuItem item)
        {
            var native = new NativeMenuItem(Header(item))
            {
                IsEnabled = item.IsEnabled && (item.Command == null || item.Command.CanExecute(item.CommandParameter)),
                ToolTip = ToolTip.GetTip(item) as string
            };
            if (item.InputGesture != null)
                native.Gesture = item.InputGesture;
            if (item.ToggleType != MenuItemToggleType.None)
            {
                native.ToggleType = item.ToggleType == MenuItemToggleType.Radio ? NativeMenuItemToggleType.Radio : NativeMenuItemToggleType.CheckBox;
                native.IsChecked = item.IsChecked;
            }
            if (item.Items.Count > 0)
            {
                var sub = new NativeMenu();
                sub.NeedsUpdate += (s, e) => Fill(sub, item.Items.ToArray());
                Fill(sub, item.Items.ToArray());
                native.Menu = sub;
            }
            else
                native.Click += (s, e) => Click(item);
            return native;
        }

        /// <summary>Does what clicking the window menu's item does: ticks it, runs its command and its Click handlers.</summary>
        internal static void Click(MenuItem item)
        {
            // The window menu ticks entries on a pointer click only, so tick them here (radio groups untick the others)
            if (item.ToggleType == MenuItemToggleType.Radio)
                item.IsChecked = true;
            else if (item.ToggleType == MenuItemToggleType.CheckBox)
                item.IsChecked = !item.IsChecked;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }

        /// <summary>
        ///     The header as the window menu shows it, without the access key mark macOS does not use: the first single
        ///     "_" goes ("_Open" → "Open") and "__" stands for "_".
        /// </summary>
        internal static string Header(MenuItem item)
        {
            var text = item.Header as string ?? item.Header?.ToString() ?? string.Empty;
            var result = new StringBuilder(text.Length);
            var marked = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '_' && i + 1 < text.Length && text[i + 1] == '_')
                {
                    result.Append('_');
                    i++;
                }
                else if (text[i] == '_' && !marked)
                    marked = true;
                else
                    result.Append(text[i]);
            }
            return result.ToString();
        }
    }
}
