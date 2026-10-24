// --------------------------------------------------
// Umami 3DS Theme Editor - Extensions.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia.Media;
using ThemeEditor.Common.Graphics;
using ThemeEditor.App.Themes;

namespace ThemeEditor.App
{
    internal static class Extensions
    {
        public static uint NextPowerOfTwo(this uint x)
        {
            x--;
            x |= (x >> 1);
            x |= (x >> 2);
            x |= (x >> 4);
            x |= (x >> 8);
            x |= (x >> 16);
            return (x + 1);
        }

        public static Color Blend(this Color bg, Color fg, float factor)
        {
            float alphaFg = fg.A / 255.0f * factor;
            float alphaBg = bg.A / 255.0f;

            float preRfg = fg.R * alphaFg;
            float preGfg = fg.G * alphaFg;
            float preBfg = fg.B * alphaFg;

            float preRbg = bg.R * alphaBg;
            float preGbg = bg.G * alphaBg;
            float preBbg = bg.B * alphaBg;

            float newAlpha = alphaFg + alphaBg * (1 - alphaFg);

            byte newR = (byte)((preRfg + preRbg * (1 - alphaFg)) / newAlpha).Clamp(0, 255);
            byte newG = (byte)((preGfg + preGbg * (1 - alphaFg)) / newAlpha).Clamp(0, 255);
            byte newB = (byte)((preBfg + preBbg * (1 - alphaFg)) / newAlpha).Clamp(0, 255);
            byte newA = (byte)(newAlpha * 255).Clamp(0, 255);

            return Color.FromArgb(newA, newR, newG, newB);
        }

        public static T Clamp<T>(this T value, T min, T max) where T : IComparable
        {
            if (value.CompareTo(max) > 0)
                return max;
            if (value.CompareTo(min) < 0)
                return min;
            return value;
        }

        public static ColorRgb888 ToColorRgb888(this Color c)
        {
            return new ColorRgb888(c.R, c.G, c.B);
        }

        public static ColorArgb8888 ToColorArgb8888(this Color c)
        {
            return new ColorArgb8888(c.A, c.R, c.G, c.B);
        }

        public static Color ToMediaColor(this ColorRgb888 c)
        {
            return Color.FromArgb(255, c.R, c.G, c.B);
        }

        public static Color ToMediaColor(this ColorArgb8888 c)
        {
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }


        public static Dictionary<string, string> GetMetadata(this ViewModelBase arg, params string[] basePath)
        {
            Dictionary<string, string> meta = new Dictionary<string, string>();
            Stack<string> pathStack = new Stack<string>(basePath);
            void GetMetadataInner(ViewModelBase ivm)
            {
                var props = ivm.GetType().GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in props)
                {
                    object value = p.GetValue(ivm);
                    if (value is ViewModelBase)
                    {
                        pathStack.Push(p.Name);
                        GetMetadataInner((ViewModelBase)value);
                        pathStack.Pop();
                    }
                    else if(p.CanRead && p.CanWrite)
                    {
                        var pathStr = string.Join(".", pathStack.Reverse()) + "." + p.Name;
                        if (value is Color || value is Boolean || value is Enum || value is double)
                        {
                            meta.Add(pathStr, Convert.ToString(value, CultureInfo.InvariantCulture));

                        }
                    }
                }
            }

            foreach (string p in basePath)
            {
                var type = arg.GetType();
                var info = type.GetProperty(p);
                if (!typeof(ViewModelBase).IsAssignableFrom(info.PropertyType))
                    return meta;
                arg = (ViewModelBase)info.GetValue(arg);
            }
            GetMetadataInner(arg);
            return meta;
        }

        public static void SetMetadata(this ViewModelBase arg, Dictionary<string, string> metadata, params string[] basePath)
        {

            void SetMetadataInner(string path, string value)
            {
                object cur = arg;
                var parts = path.Split('.');
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    var type = cur.GetType();
                    var info = type.GetProperty(part, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);
                    if (info == null)
                        return;
                    if (i == parts.Length - 1)
                    {
                        try
                        {
                            if (info.PropertyType == typeof(Color))
                            {

                                var col = Color.Parse(value);
                                info.SetValue(cur, col);

                            }
                            else if (info.PropertyType == typeof(double))
                            {
                                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var res))
                                    info.SetValue(cur, res);
                            }
                            else if (info.PropertyType == typeof(bool))
                            {
                                if (bool.TryParse(value, out var res))
                                    info.SetValue(cur, res);
                            }
                            else if (info.PropertyType.IsEnum)
                            {
                                if (!Enum.IsDefined(info.PropertyType, value))
                                    return;
                                info.SetValue(cur, Enum.Parse(info.PropertyType, value));
                            }
                        }
                        catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
                        {
                        }

                    }
                    else
                    {
                        cur = info.GetValue(cur);
                    }
                }
            }

            var append = string.Join(".", basePath);
            if (append.Length > 0)
                append += ".";

            foreach (var pair in metadata)
            {
                SetMetadataInner(append + pair.Key, pair.Value);
            }
        }

    }
}