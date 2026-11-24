using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// EditorWindow の中身を内部 API の GUIView.GrabPixels で撮る（capture-window と auto-play の capture で共用）。
    /// </summary>
    /// <remarks>
    /// GrabPixels は Unity の内部 API なので、シグネチャが見つからなければ false を返す。
    /// GrabPixels が返す絵は上下逆なので、ここで反転してから返す。
    /// </remarks>
    internal static class EditorWindowCapture
    {
        /// <summary>
        /// window の中身を撮る。pixels は Texture2D.SetPixels にそのまま渡せる順（下の行から）で、上下は正しい向き。
        /// </summary>
        internal static bool TryGrabPixels(EditorWindow window, out Color[] pixels, out int width, out int height)
        {
            pixels = null;
            width = 0;
            height = 0;

            RenderTexture rt = null;
            Texture2D texture = null;
            // rt を破棄する前に元へ戻すため、先に保存する（null も復元対象）
            var prevActive = RenderTexture.active;

            try
            {
                var parentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance);
                if (parentField == null) return false;

                var hostView = parentField.GetValue(window);
                if (hostView == null) return false;

                var grabPixelsMethod = hostView.GetType().GetMethod("GrabPixels",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(RenderTexture), typeof(Rect) },
                    null);
                if (grabPixelsMethod == null) return false;

                // DPI スケーリングを考慮（Retina ディスプレイ等）。GrabPixels の rect は実際のピクセルサイズで指定する
                var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                var position = window.position;
                width = (int)(position.width * pixelsPerPoint);
                height = (int)(position.height * pixelsPerPoint);

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                grabPixelsMethod.Invoke(hostView, new object[] { rt, new Rect(0, 0, width, height) });

                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                pixels = texture.GetPixels();
                FlipVerticallyInPlace(pixels, width, height);
                return true;
            }
            catch
            {
                pixels = null;
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (texture != null) Object.DestroyImmediate(texture);
                if (rt != null) Object.DestroyImmediate(rt);
            }
        }

        /// <summary>window の中身を撮って PNG にする。失敗したら null</summary>
        internal static byte[] TryGrabPng(EditorWindow window, out int width, out int height)
        {
            if (!TryGrabPixels(window, out var pixels, out width, out height))
                return null;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>ピクセル配列を上下反転する（一時配列を使わない）</summary>
        private static void FlipVerticallyInPlace(Color[] pixels, int width, int height)
        {
            for (int y = 0; y < height / 2; y++)
            {
                int srcRow = y * width;
                int dstRow = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    var temp = pixels[srcRow + x];
                    pixels[srcRow + x] = pixels[dstRow + x];
                    pixels[dstRow + x] = temp;
                }
            }
        }
    }
}
