// --------------------------------------------------
// Umami 3DS Theme Editor - PropertyEditors.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ThemeEditor.App.Localization;
using ThemeEditor.App.Themes;
using DescriptionAttribute = ThemeEditor.App.Localization.DescriptionAttribute;
using DisplayNameAttribute = ThemeEditor.App.Localization.DisplayNameAttribute;

namespace ThemeEditor.App.Controls
{
    /// <summary>
    ///     Wraps one property of a view model for the generated editors: carries its localized name, description,
    ///     order and range, and raises PropertyChanged("Value") when the underlying property changes.
    /// </summary>
    public sealed class PropertyProxy : INotifyPropertyChanged, IDisposable
    {
        public PropertyInfo Property { get; }
        public object Target { get; }

        public string Name { get; }
        public string Description { get; }
        public int Order { get; }
        public double RangeMin { get; }
        public double RangeMax { get; }

        public object Value
        {
            get { return Property.CanRead ? Property.GetValue(Target, null) : null; }
            set
            {
                if (Property.CanWrite)
                    Property.SetValue(Target, value, null);
            }
        }

        public Type ValueType => Property.PropertyType;

        public PropertyProxy(PropertyInfo pi, object target)
        {
            Name = pi.GetCustomAttribute<DisplayNameAttribute>(false)?.DisplayName ?? pi.Name.ToUpper();
            Description = pi.GetCustomAttribute<DescriptionAttribute>(false)?.Description ?? "";
            Order = pi.GetCustomAttribute<OrderAttribute>(false)?.Order ?? int.MaxValue;
            RangeMin = pi.GetCustomAttribute<RangeAttribute>(false)?.Minimum ?? 0;
            RangeMax = pi.GetCustomAttribute<RangeAttribute>(false)?.Maximum ?? 1;
            Property = pi;
            Target = target;

            var npc = target as INotifyPropertyChanged;
            if (npc != null)
                npc.PropertyChanged += TargetOnPropertyChanged;
        }

        private void TargetOnPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != Property.Name)
                return;
            if (Dispatcher.UIThread.CheckAccess())
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            else
                Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))));
        }

        public void Dispose()
        {
            var npc = Target as INotifyPropertyChanged;
            if (npc != null)
                npc.PropertyChanged -= TargetOnPropertyChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Palette for the color picker: first row from the top screen image, second from the bottom.</summary>
    internal sealed class ImagePalette : IColorPalette
    {
        private readonly List<Color> _top;
        private readonly List<Color> _bottom;

        public ImagePalette(IEnumerable<Color> top, IEnumerable<Color> bottom)
        {
            _top = (top ?? Enumerable.Empty<Color>()).ToList();
            _bottom = (bottom ?? Enumerable.Empty<Color>()).ToList();
        }

        public int ColorCount => Math.Max(1, Math.Max(_top.Count, _bottom.Count));
        public int ShadeCount => 2;

        public Color GetColor(int colorIndex, int shadeIndex)
        {
            var list = shadeIndex == 0 ? _top : _bottom;
            return colorIndex >= 0 && colorIndex < list.Count ? list[colorIndex] : Color.FromArgb(0, 0, 0, 0);
        }
    }

    /// <summary>Builds the Colors and Flags tab contents straight from the view model attributes.</summary>
    public sealed class PropertyEditors : IDisposable
    {
        public static readonly IValueConverter BoolToOpacity =
            new FuncValueConverter<bool, double>(b => b ? 1.0 : 0.5);

        private readonly List<PropertyProxy> _proxies = new List<PropertyProxy>();
        private readonly List<ColorView> _pickers = new List<ColorView>();
        private ThemeViewModel _theme;

        public static IEnumerable<PropertyInfo> VisibleProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(o => o.GetCustomAttribute<VisibleAttribute>()?.Visible ?? true)
                .OrderBy(o => o.GetCustomAttribute<OrderAttribute>()?.Order ?? int.MaxValue);
        }

        private IEnumerable<PropertyProxy> Unroll(object vm)
        {
            foreach (var info in VisibleProperties(vm.GetType()))
            {
                var proxy = new PropertyProxy(info, vm);
                _proxies.Add(proxy);
                yield return proxy;
            }
        }

        public void Dispose()
        {
            foreach (var p in _proxies)
                p.Dispose();
            _proxies.Clear();
            _pickers.Clear();
        }

        /// <summary>Re-reads the image palettes (call after the top/bottom texture changed).</summary>
        public void RefreshPalettes()
        {
            if (_theme == null)
                return;
            foreach (var p in _pickers)
                p.Palette = new ImagePalette(_theme.Textures.Top.Palette, _theme.Textures.Bottom.Palette);
        }

        // ---------------------------------------------------------------- Colors tab

        public Control BuildColors(ThemeViewModel theme)
        {
            _theme = theme;
            var root = new StackPanel();
            foreach (var set in Unroll(theme.Colors))
            {
                var setVm = set.Value as ViewModelBase;
                if (setVm == null)
                    continue;

                var title = new TextBlock { Text = set.Name, FontWeight = FontWeight.Bold };
                var desc = new TextBlock { Text = set.Description, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap };
                var wrap = new WrapPanel();

                foreach (var prop in Unroll(setVm))
                {
                    var editor = BuildColorEditor(prop);
                    if (editor != null)
                        wrap.Children.Add(editor);
                }

                var panel = new StackPanel
                {
                    Margin = new Thickness(3, 0),
                    Children = { title, desc, wrap, new Separator { Margin = new Thickness(3) } }
                };
                panel.Bind(Visual.OpacityProperty,
                    new Binding(nameof(ViewModelBase.Enabled)) { Source = setVm, Converter = BoolToOpacity });
                root.Children.Add(panel);
            }
            return root;
        }

        private Control BuildColorEditor(PropertyProxy prop)
        {
            var type = prop.ValueType;
            if (type == typeof(Color))
                return BuildColor(prop);
            if (type == typeof(bool))
                return BuildToggle(prop, 125, true);
            if (type == typeof(double))
                return BuildSlider(prop);
            return null;
        }

        private Control BuildColor(PropertyProxy prop)
        {
            var picker = new ColorPicker
            {
                Width = 101,
                IsAlphaVisible = false,
                IsAlphaEnabled = false,
                IsColorModelVisible = true,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            picker.Bind(ColorView.ColorProperty, new Binding(nameof(PropertyProxy.Value), BindingMode.TwoWay) { Source = prop });
            picker.Palette = new ImagePalette(_theme?.Textures.Top.Palette, _theme?.Textures.Bottom.Palette);
            _pickers.Add(picker);

            var eyedropper = new ColorEyedropper { Margin = new Thickness(-1, 0, 0, 0) };
            eyedropper.Bind(ColorEyedropper.PickedColorProperty, new Binding(nameof(PropertyProxy.Value), BindingMode.TwoWay) { Source = prop });

            // Copy/paste of colors through the clipboard
            var menu = new ContextMenu();
            var copy = new MenuItem { Header = MainResources.Menu_Copy };
            copy.Click += async (s, e) =>
            {
                var clip = TopLevel.GetTopLevel(picker)?.Clipboard;
                if (clip != null)
                    await clip.SetTextAsync(ToHex((Color) prop.Value));
            };
            var paste = new MenuItem { Header = MainResources.Menu_Paste };
            paste.Click += async (s, e) =>
            {
                var clip = TopLevel.GetTopLevel(picker)?.Clipboard;
                var text = clip == null ? null : await clip.GetTextAsync();
                Color parsed;
                if (TryParseColor(text, out parsed))
                    prop.Value = parsed;
            };
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            picker.ContextMenu = menu;

            var tip = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = prop.Name, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = prop.Description, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 }
                }
            };
            ToolTip.SetTip(picker, tip);

            return new StackPanel
            {
                Margin = new Thickness(3),
                Width = 125,
                Children =
                {
                    new TextBlock { Text = prop.Name + ":", FontSize = 10, Margin = new Thickness(3), TextTrimming = TextTrimming.CharacterEllipsis },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { picker, eyedropper } }
                }
            };
        }

        private static string ToHex(Color c)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static bool TryParseColor(string text, out Color color)
        {
            color = default(Color);
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var open = text.IndexOf('(');
                var close = text.IndexOf(')');
                if (open < 0 || close < open)
                    return false;
                var parts = text.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length < 3)
                    return false;
                byte r, g, b;
                if (!byte.TryParse(parts[0].Trim(), out r) || !byte.TryParse(parts[1].Trim(), out g) || !byte.TryParse(parts[2].Trim(), out b))
                    return false;
                color = Color.FromRgb(r, g, b);
                return true;
            }
            Color parsed;
            if (!Color.TryParse(text, out parsed))
                return false;
            color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
            return true;
        }

        private Control BuildSlider(PropertyProxy prop)
        {
            var label = new TextBlock { FontSize = 10, Margin = new Thickness(3), TextTrimming = TextTrimming.CharacterEllipsis };
            var slider = new Slider
            {
                Minimum = prop.RangeMin,
                Maximum = prop.RangeMax,
                TickFrequency = 0.05,
                SmallChange = 0.05,
                Width = 115,
                Margin = new Thickness(3, 0)
            };
            slider.Bind(RangeBase.ValueProperty, new Binding(nameof(PropertyProxy.Value), BindingMode.TwoWay) { Source = prop });
            Action update = () => label.Text = $"{prop.Name}: {(double) prop.Value:f2}";
            prop.PropertyChanged += (s, e) => update();
            update();

            ToolTip.SetTip(slider, new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = prop.Name, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = prop.Description, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 }
                }
            });

            return new StackPanel { Margin = new Thickness(3), Width = 125, Children = { label, slider } };
        }

        // ---------------------------------------------------------------- Shared toggle

        private Control BuildToggle(PropertyProxy prop, double width, bool withLabel)
        {
            var toggle = new ToggleButton
            {
                Width = width,
                Margin = new Thickness(3),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            Action sync = () =>
            {
                var on = (bool) prop.Value;
                if (toggle.IsChecked != on)
                    toggle.IsChecked = on;
                toggle.Content = on ? MainResources.Control_Toggle_True : MainResources.Control_Toggle_False;
            };
            toggle.IsCheckedChanged += (s, e) =>
            {
                var on = toggle.IsChecked == true;
                if ((bool) prop.Value != on)
                    prop.Value = on;
                toggle.Content = on ? MainResources.Control_Toggle_True : MainResources.Control_Toggle_False;
            };
            prop.PropertyChanged += (s, e) => sync();
            sync();

            if (!withLabel)
                return toggle;

            return new StackPanel
            {
                Margin = new Thickness(3),
                Width = 125,
                Children = { new TextBlock { Text = prop.Name, FontSize = 10, Margin = new Thickness(3), TextTrimming = TextTrimming.CharacterEllipsis }, toggle }
            };
        }

        // ---------------------------------------------------------------- Flags tab

        public Control BuildFlags(ThemeViewModel theme)
        {
            _theme = theme;
            var root = new StackPanel();
            foreach (var prop in Unroll(theme.Flags))
            {
                Control editor = null;
                if (prop.ValueType == typeof(bool))
                    editor = BuildToggle(prop, double.NaN, false);
                else if (prop.ValueType.IsEnum)
                    editor = BuildEnum(prop);
                if (editor == null)
                    continue;

                editor.HorizontalAlignment = HorizontalAlignment.Stretch;
                ToolTip.SetTip(editor, new TextBlock
                {
                    Text = prop.Description,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 300
                });

                var grid = new Grid
                {
                    Margin = new Thickness(3, 0),
                    ColumnDefinitions = new ColumnDefinitions("*,*")
                };
                var name = new TextBlock
                {
                    Text = prop.Name,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(editor, 1);
                grid.Children.Add(name);
                grid.Children.Add(editor);

                root.Children.Add(new StackPanel { Children = { grid, new Separator { Margin = new Thickness(3) } } });
            }
            return root;
        }

        private sealed class EnumItem
        {
            public string Name { get; set; }
            public object Value { get; set; }
            public override string ToString() => Name;
        }

        private Control BuildEnum(PropertyProxy prop)
        {
            var type = prop.ValueType;
            var items = type.GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(f => f.GetCustomAttribute<VisibleAttribute>()?.Visible ?? true)
                .OrderBy(f => f.GetCustomAttribute<OrderAttribute>()?.Order ?? int.MaxValue)
                .Select(f => new EnumItem
                {
                    Value = f.GetValue(null),
                    Name = f.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? f.Name
                })
                .ToList();

            var combo = new ComboBox { ItemsSource = items, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch };
            var updating = false;
            Action sync = () =>
            {
                updating = true;
                combo.SelectedItem = items.FirstOrDefault(i => i.Value.Equals(prop.Value));
                updating = false;
            };
            combo.SelectionChanged += (s, e) =>
            {
                if (updating)
                    return;
                var item = combo.SelectedItem as EnumItem;
                if (item != null && !item.Value.Equals(prop.Value))
                    prop.Value = item.Value;
                // The view model rules may have corrected the value; reflect what it ended up with
                sync();
            };
            prop.PropertyChanged += (s, e) => sync();
            sync();
            return combo;
        }
    }
}
