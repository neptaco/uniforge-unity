using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// スプライトアセットを生成するツール
    /// </summary>
    [Tool("create-sprite",
        Description = "Create a sprite asset (PNG) with a specified shape and color, saved to Assets/",
        Title = "Create Sprite",
        Category = ToolCategory.Asset,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class CreateSpriteHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Sprite name", Required = true)]
            public string name;

            [ToolParameter("Shape: square, circle, triangle, hexagon, diamond", Required = false)]
            public string shape;

            [ToolParameter("Size [width, height] in pixels (default: [64, 64])", Required = false)]
            public float[] size;

            [ToolParameter("Color [r, g, b, a] in 0-1 range (default: white)", Required = false)]
            public float[] color;

            [ToolParameter("Save path under project (default: Assets/Sprites/<name>.png)", Required = false)]
            public string save_path;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string name;
            public string path;
            public int width;
            public int height;
            public string shape;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var spriteName = args.GetString("name");

            if (string.IsNullOrEmpty(spriteName))
            {
                return ToolResult.Fail("Parameter 'name' is required");
            }

            var shape = args.GetString("shape") ?? "square";
            var sizeArr = args.GetFloatArray("size");
            int width = (sizeArr != null && sizeArr.Length >= 1) ? (int)sizeArr[0] : 64;
            int height = (sizeArr != null && sizeArr.Length >= 2) ? (int)sizeArr[1] : width;

            var colorArr = args.GetFloatArray("color");
            Color fillColor = Color.white;
            if (colorArr != null && colorArr.Length >= 3)
            {
                fillColor = new Color(
                    colorArr[0], colorArr[1], colorArr[2],
                    colorArr.Length >= 4 ? colorArr[3] : 1f
                );
            }

            var savePath = args.GetString("save_path");
            if (string.IsNullOrEmpty(savePath))
            {
                savePath = $"Assets/Sprites/{spriteName}.png";
            }

            // ディレクトリ作成
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var fullDir = Path.Combine(Application.dataPath, "..", dir);
                Directory.CreateDirectory(fullDir);
            }

            // テクスチャ生成
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            try
            {
                switch (shape.ToLowerInvariant())
                {
                    case "square":
                        FillSquare(tex, fillColor);
                        break;
                    case "circle":
                        FillCircle(tex, fillColor);
                        break;
                    case "triangle":
                        FillTriangle(tex, fillColor);
                        break;
                    case "hexagon":
                        FillRegularPolygon(tex, fillColor, 6);
                        break;
                    case "diamond":
                        FillRegularPolygon(tex, fillColor, 4);
                        break;
                    default:
                        return ToolResult.Fail(
                            $"Invalid shape: {shape}. Valid shapes: square, circle, triangle, hexagon, diamond");
                }

                tex.Apply();

                // PNG 保存
                var pngBytes = tex.EncodeToPNG();
                var fullPath = Path.Combine(Application.dataPath, "..", savePath);
                File.WriteAllBytes(fullPath, pngBytes);

                // アセットデータベース更新
                AssetDatabase.Refresh();

                // TextureImporter 設定をスプライト用に変更
                var importer = AssetImporter.GetAtPath(savePath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spritePixelsPerUnit = Mathf.Max(width, height);
                    importer.filterMode = shape == "square" ? FilterMode.Point : FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }

                return ToolResult.Ok(new Output
                {
                    success = true,
                    name = spriteName,
                    path = savePath,
                    width = width,
                    height = height,
                    shape = shape,
                    message = $"Sprite '{spriteName}' created at {savePath} ({width}x{height})"
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static void FillSquare(Texture2D tex, Color color)
        {
            var pixels = new Color[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
        }

        private static void FillCircle(Texture2D tex, Color color)
        {
            int w = tex.width, h = tex.height;
            float cx = w / 2f, cy = h / 2f;
            float radius = Mathf.Min(cx, cy) - 1f;
            float radiusSq = radius * radius;

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    pixels[y * w + x] = (dx * dx + dy * dy) <= radiusSq ? color : Color.clear;
                }
            }
            tex.SetPixels(pixels);
        }

        private static void FillTriangle(Texture2D tex, Color color)
        {
            int w = tex.width, h = tex.height;
            Vector2 top = new Vector2(w / 2f, h - 1);
            Vector2 left = new Vector2(0, 0);
            Vector2 right = new Vector2(w - 1, 0);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x, y);
                    pixels[y * w + x] = PointInTriangle(p, top, left, right) ? color : Color.clear;
                }
            }
            tex.SetPixels(pixels);
        }

        private static void FillRegularPolygon(Texture2D tex, Color color, int sides)
        {
            int w = tex.width, h = tex.height;
            float cx = w / 2f, cy = h / 2f;
            float radius = Mathf.Min(cx, cy) - 1f;

            var vertices = new Vector2[sides];
            float angleStep = 360f / sides;
            for (int i = 0; i < sides; i++)
            {
                float angle = (90f + i * angleStep) * Mathf.Deg2Rad;
                vertices[i] = new Vector2(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius);
            }

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x, y);
                    pixels[y * w + x] = PointInPolygon(p, vertices) ? color : Color.clear;
                }
            }
            tex.SetPixels(pixels);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static bool PointInPolygon(Vector2 p, Vector2[] vertices)
        {
            int n = vertices.Length;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((vertices[i].y > p.y) != (vertices[j].y > p.y) &&
                    p.x < (vertices[j].x - vertices[i].x) * (p.y - vertices[i].y) /
                           (vertices[j].y - vertices[i].y) + vertices[i].x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
