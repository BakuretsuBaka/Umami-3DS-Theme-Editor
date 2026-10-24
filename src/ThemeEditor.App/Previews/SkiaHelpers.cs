// --------------------------------------------------
// Umami 3DS Theme Editor - SkiaHelpers.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;

namespace ThemeEditor.App.Previews
{
    /// <summary>Gradient stop with WPF semantics (offset may lie outside [0, 1]).</summary>
    public struct Stop
    {
        public float Offset;
        public SKColor Color;

        public Stop(float offset, SKColor color)
        {
            Offset = offset;
            Color = color;
        }

        public Stop(float offset, Color color)
        {
            Offset = offset;
            Color = color.ToSk();
        }
    }

    /// <summary>
    ///     Drawing helpers that mirror the WPF primitives the original editor used for its previews
    ///     (bounding-box relative gradients, geometry combination, blur and drop shadow effects).
    /// </summary>
    internal static class SkiaHelpers
    {
        public static SKColor ToSk(this Color c)
        {
            return new SKColor(c.R, c.G, c.B, c.A);
        }

        public static SKColor WithOpacity(this SKColor c, double opacity)
        {
            return c.WithAlpha((byte) Math.Max(0, Math.Min(255, c.Alpha * opacity)));
        }

        public static SKColor Hex(string rgb)
        {
            return SKColor.Parse(rgb);
        }

        public static SKPaint FillPaint(SKColor c)
        {
            return new SKPaint { Style = SKPaintStyle.Fill, Color = c, IsAntialias = true };
        }

        public static SKPaint StrokePaint(SKColor c, float width)
        {
            return new SKPaint { Style = SKPaintStyle.Stroke, Color = c, StrokeWidth = width, IsAntialias = true };
        }

        /// <summary>
        ///     Linear gradient laid out like WPF's RelativeToBoundingBox brush: start/end are fractions of the
        ///     bounds and stop offsets may exceed [0, 1] (colors pad outside).
        /// </summary>
        public static SKShader Gradient(SKRect bounds, float sx, float sy, float ex, float ey, params Stop[] stops)
        {
            var sorted = new List<Stop>(stops);
            var lo = float.MaxValue;
            var hi = float.MinValue;
            foreach (var s in sorted)
            {
                lo = Math.Min(lo, s.Offset);
                hi = Math.Max(hi, s.Offset);
            }
            if (hi - lo < 1e-6f)
                hi = lo + 1e-6f;

            var p0 = new SKPoint(bounds.Left + (sx + (ex - sx) * lo) * bounds.Width,
                bounds.Top + (sy + (ey - sy) * lo) * bounds.Height);
            var p1 = new SKPoint(bounds.Left + (sx + (ex - sx) * hi) * bounds.Width,
                bounds.Top + (sy + (ey - sy) * hi) * bounds.Height);

            var colors = new SKColor[sorted.Count];
            var pos = new float[sorted.Count];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = sorted[i].Color;
                pos[i] = (sorted[i].Offset - lo) / (hi - lo);
            }
            return SKShader.CreateLinearGradient(p0, p1, colors, pos, SKShaderTileMode.Clamp);
        }

        public static SKShader VGradient(SKRect bounds, params Stop[] stops)
        {
            return Gradient(bounds, 0, 0, 0, 1, stops);
        }

        // ---- Geometry

        public static SKPath RoundRect(float x, float y, float w, float h, float rx = 0, float ry = 0)
        {
            var p = new SKPath();
            if (rx > 0 || ry > 0)
                p.AddRoundRect(new SKRect(x, y, x + w, y + h), rx, ry);
            else
                p.AddRect(new SKRect(x, y, x + w, y + h));
            return p;
        }

        public static SKPath Ellipse(float cx, float cy, float rx, float ry)
        {
            var p = new SKPath();
            p.AddOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry));
            return p;
        }

        public static SKPath Parse(string svg)
        {
            var p = SKPath.ParseSvgPathData(svg);
            if (p == null)
                throw new ArgumentException("Bad path data: " + svg);
            return p;
        }

        /// <summary>Boolean combination (WPF CombinedGeometry).</summary>
        public static SKPath Combine(SKPath a, SKPath b, SKPathOp op)
        {
            var r = new SKPath();
            if (!a.Op(b, op, r))
                throw new InvalidOperationException("Path op failed");
            return r;
        }

        // ---- Drawing

        public static void FillPath(SKCanvas c, SKPath path, SKColor color)
        {
            using (var p = FillPaint(color))
                c.DrawPath(path, p);
        }

        public static void FillPath(SKCanvas c, SKPath path, SKShader shader)
        {
            using (var p = new SKPaint { Style = SKPaintStyle.Fill, Shader = shader, IsAntialias = true })
                c.DrawPath(path, p);
        }

        public static void StrokePath(SKCanvas c, SKPath path, SKColor color, float width)
        {
            using (var p = StrokePaint(color, width))
                c.DrawPath(path, p);
        }

        public static void StrokePath(SKCanvas c, SKPath path, SKShader shader, float width)
        {
            using (var p = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = width, Shader = shader, IsAntialias = true })
                c.DrawPath(path, p);
        }

        /// <summary>Runs <paramref name="draw" /> inside a layer filtered with a Gaussian blur.</summary>
        public static void WithBlur(SKCanvas c, float wpfRadius, Action draw, double opacity = 1)
        {
            using (var filter = SKImageFilter.CreateBlur(wpfRadius / 2f, wpfRadius / 2f))
            using (var paint = new SKPaint { ImageFilter = filter, Color = SKColors.White.WithOpacity(opacity) })
            {
                c.SaveLayer(paint);
                draw();
                c.Restore();
            }
        }

        /// <summary>
        ///     Draws <paramref name="draw" /> with a WPF style DropShadowEffect (shadow is drawn beneath).
        /// </summary>
        public static void WithDropShadow(SKCanvas c, SKColor color, double opacity, float blurRadius, float depth,
            float directionDegrees, Action draw)
        {
            var rad = directionDegrees * Math.PI / 180.0;
            // WPF direction 0 = right, increasing counter-clockwise on screen; 315 is the default (down-right)
            var dx = (float) (Math.Cos(rad) * depth);
            var dy = (float) (-Math.Sin(rad) * depth);
            var shadowColor = color.WithOpacity(opacity);
            var sigma = Math.Max(blurRadius / 2f, 0.01f);
            using (var filter = SKImageFilter.CreateDropShadow(dx, dy, sigma, sigma, shadowColor))
            using (var paint = new SKPaint { ImageFilter = filter })
            {
                c.SaveLayer(paint);
                draw();
                c.Restore();
            }
        }

        /// <summary>Runs <paramref name="draw" /> in a layer whose alpha is multiplied by a vertical gradient mask.</summary>
        public static void WithOpacityMask(SKCanvas c, SKRect maskBounds, Func<SKRect, SKShader> maskShader, Action draw)
        {
            c.SaveLayer(null);
            draw();
            using (var shader = maskShader(maskBounds))
            using (var mask = new SKPaint { Shader = shader, BlendMode = SKBlendMode.DstIn })
                c.DrawRect(maskBounds, mask);
            c.Restore();
        }

        /// <summary>Draws a bitmap like an ImageBrush with Stretch=Uniform and an absolute viewbox.</summary>
        public static void DrawImageUniform(SKCanvas c, SKBitmap bmp, SKRect viewbox, SKRect dest, bool mirror = false)
        {
            var scale = Math.Min(dest.Width / viewbox.Width, dest.Height / viewbox.Height);
            var w = viewbox.Width * scale;
            var h = viewbox.Height * scale;
            var target = new SKRect(dest.Left + (dest.Width - w) / 2, dest.Top + (dest.Height - h) / 2, 0, 0)
            {
                Right = dest.Left + (dest.Width - w) / 2 + w,
                Bottom = dest.Top + (dest.Height - h) / 2 + h
            };
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true })
            {
                if (mirror)
                {
                    c.Save();
                    c.Scale(-1, 1, (dest.Left + dest.Right) / 2, 0);
                    c.DrawBitmap(bmp, viewbox, target, paint);
                    c.Restore();
                }
                else
                {
                    c.DrawBitmap(bmp, viewbox, target, paint);
                }
            }
        }
    }
}
