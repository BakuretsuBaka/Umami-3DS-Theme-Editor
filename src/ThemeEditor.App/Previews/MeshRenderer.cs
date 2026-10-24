// --------------------------------------------------
// Umami 3DS Theme Editor - MeshRenderer.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Avalonia.Platform;
using SkiaSharp;

namespace ThemeEditor.App.Previews
{
    /// <summary>Triangle soup with per-corner normals, loaded from a Wavefront .obj asset.</summary>
    internal sealed class Mesh
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public int TriangleCount => Positions.Length / 3;

        public static Mesh Load(string assetName)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();

            using (var s = AssetLoader.Open(new Uri("avares://UmamiThemeEditor/Resources/" + assetName)))
            using (var sr = new StreamReader(s, Encoding.ASCII))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    var ls = line.Split(' ');
                    switch (ls[0])
                    {
                        case "v":
                            verts.Add(Vec(ls));
                            break;
                        case "vn":
                            normals.Add(Vec(ls));
                            break;
                        case "f":
                            for (var i = 1; i <= 3; i++)
                            {
                                var parts = ls[i].Split('/');
                                pos.Add(verts[int.Parse(parts[0], CultureInfo.InvariantCulture) - 1]);
                                nrm.Add(normals[int.Parse(parts[2], CultureInfo.InvariantCulture) - 1]);
                            }
                            break;
                    }
                }
            }
            return new Mesh { Positions = pos.ToArray(), Normals = nrm.ToArray() };
        }

        private static Vector3 Vec(string[] ls)
        {
            return new Vector3(
                float.Parse(ls[1], NumberStyles.Any, CultureInfo.InvariantCulture),
                float.Parse(ls[2], NumberStyles.Any, CultureInfo.InvariantCulture),
                float.Parse(ls[3], NumberStyles.Any, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Material roughly equivalent to the WPF Diffuse + Specular + Emissive group the original used.</summary>
    internal struct MeshMaterial
    {
        public Vector3 Diffuse;
        public Vector3 Specular;

        public MeshMaterial(SKColor diffuse, SKColor specular)
        {
            Diffuse = new Vector3(diffuse.Red, diffuse.Green, diffuse.Blue) / 255f;
            Specular = new Vector3(specular.Red, specular.Green, specular.Blue) / 255f;
        }
    }

    /// <summary>
    ///     Tiny z-buffered software rasterizer (per-vertex lighting, Gouraud shading, 2x supersampling).
    ///     Replaces the WPF Viewport3D so the 3D folder preview works on every platform.
    /// </summary>
    internal sealed class MeshRenderer
    {
        private const int Width = 412;
        private const int Height = 240;
        private const int Scale = 2;

        // Perspective camera: horizontal FOV 30 deg, located at (0, 1, 44.786) looking down -Z
        private static readonly Vector3 CameraPos = new Vector3(0, 1, 44.786f);

        private static readonly float Focal = (float) (Width * Scale / 2.0 / Math.Tan(15 * Math.PI / 180));

        private static readonly Vector3 AmbientLight = new Vector3(0.5f);

        private static readonly (Vector3 Pos, float Linear)[] Lights =
        {
            (new Vector3(-10, 10, 10), 0.25f),
            (new Vector3(0, 0, 50), 0.2f),
            (new Vector3(10, -10, 10), 0.25f)
        };

        private readonly float[] _depth = new float[Width * Scale * Height * Scale];
        private readonly byte[] _pixels = new byte[Width * Scale * Height * Scale * 4];

        private Mesh _folder;
        private Mesh _file;

        public void EnsureLoaded()
        {
            if (_folder == null)
                _folder = Mesh.Load("folder.obj");
            if (_file == null)
                _file = Mesh.Load("file.obj");
        }

        /// <summary>Renders both meshes and draws the result (transparent background) at the canvas origin.</summary>
        public void Draw(SKCanvas canvas, MeshMaterial folder, MeshMaterial file, double rotationDegrees, double bounceY)
        {
            EnsureLoaded();
            Array.Clear(_pixels, 0, _pixels.Length);
            for (var i = 0; i < _depth.Length; i++)
                _depth[i] = float.MaxValue;

            var rot = Matrix4x4.CreateRotationY((float) (rotationDegrees * Math.PI / 180)) *
                      Matrix4x4.CreateTranslation(0, (float) bounceY, 0);
            Rasterize(_folder, folder, rot);
            Rasterize(_file, file, rot);

            var info = new SKImageInfo(Width * Scale, Height * Scale, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var bmp = new SKBitmap())
            {
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(_pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    bmp.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
                    using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
                        canvas.DrawBitmap(bmp, new SKRect(0, 0, Width, Height), paint);
                }
                finally
                {
                    handle.Free();
                }
            }
        }

        private void Rasterize(Mesh mesh, MeshMaterial mat, Matrix4x4 world)
        {
            var n = mesh.Positions.Length;
            var sx = new float[n];
            var sy = new float[n];
            var sz = new float[n];
            var col = new Vector3[n];

            for (var i = 0; i < n; i++)
            {
                var p = Vector3.Transform(mesh.Positions[i], world);
                var nrm = Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[i], world));
                col[i] = Light(p, nrm, mat);

                var depth = CameraPos.Z - p.Z;
                if (depth < 26.5f)
                    depth = 26.5f;
                sx[i] = Width * Scale / 2f + Focal * (p.X - CameraPos.X) / depth;
                sy[i] = Height * Scale / 2f - Focal * (p.Y - CameraPos.Y) / depth;
                sz[i] = depth;
            }

            for (var t = 0; t < n; t += 3)
            {
                var x0 = sx[t]; var y0 = sy[t];
                var x1 = sx[t + 1]; var y1 = sy[t + 1];
                var x2 = sx[t + 2]; var y2 = sy[t + 2];

                // Counter-clockwise (in a Y-up world) is front-facing; screen Y is flipped so the sign is inverted.
                var area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (area >= 0)
                    continue;

                var minX = Math.Max(0, (int) Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
                var maxX = Math.Min(Width * Scale - 1, (int) Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
                var minY = Math.Max(0, (int) Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
                var maxY = Math.Min(Height * Scale - 1, (int) Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
                var inv = 1f / area;

                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    var w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) * inv;
                    var w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) * inv;
                    var w2 = 1f - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                        continue;

                    var z = w0 * sz[t] + w1 * sz[t + 1] + w2 * sz[t + 2];
                    var idx = y * Width * Scale + x;
                    if (z >= _depth[idx])
                        continue;
                    _depth[idx] = z;

                    var c = w0 * col[t] + w1 * col[t + 1] + w2 * col[t + 2];
                    var o = idx * 4;
                    _pixels[o] = ToByte(c.Z);
                    _pixels[o + 1] = ToByte(c.Y);
                    _pixels[o + 2] = ToByte(c.X);
                    _pixels[o + 3] = 255;
                }
            }
        }

        private static byte ToByte(float v)
        {
            return (byte) (Math.Max(0, Math.Min(1, v)) * 255 + 0.5f);
        }

        private static Vector3 Light(Vector3 p, Vector3 n, MeshMaterial mat)
        {
            var view = Vector3.Normalize(CameraPos - p);
            var color = AmbientLight * mat.Diffuse;
            foreach (var light in Lights)
            {
                var l = light.Pos - p;
                var d = l.Length();
                if (d > 100)
                    continue;
                var ld = l / d;
                var atten = 1f / (1f + light.Linear * d);
                var ndl = Math.Max(0f, Vector3.Dot(n, ld));
                color += mat.Diffuse * ndl * atten;

                var h = Vector3.Normalize(ld + view);
                var ndh = Math.Max(0f, Vector3.Dot(n, h));
                color += mat.Specular * (float) Math.Pow(ndh, 10) * atten;
                color += new Vector3(1f) * (float) Math.Pow(ndh, 50) * atten;
            }
            // Emissive white at 12.5% alpha
            color += new Vector3(0.125f);
            return color;
        }
    }
}
