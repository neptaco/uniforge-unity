using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UniForge.Services;
using UniForge.Tools;

namespace UniForge.Tests
{
    /// <summary>
    /// EditorWindow を撮った画像の上下の向きのテスト。
    /// GrabPixels は上下逆の絵を返すので、反転してから PNG にしないと上下逆の画像になる。
    /// </summary>
    public class WindowCaptureOrientationTests
    {
        /// <summary>上半分を赤、下半分を青で塗るウィンドウ</summary>
        private sealed class OrientationProbeWindow : EditorWindow
        {
            public int PaintCount { get; private set; }

            private void OnGUI()
            {
                if (Event.current.type != EventType.Repaint)
                    return;

                var half = position.height / 2f;
                EditorGUI.DrawRect(new Rect(0, 0, position.width, half), Color.red);
                EditorGUI.DrawRect(new Rect(0, half, position.width, position.height - half), Color.blue);
                PaintCount++;
            }
        }

        private OrientationProbeWindow _window;
        private string _outputPath;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
                _window.Close();
            if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
                File.Delete(_outputPath);
        }

        [UnityTest]
        public IEnumerator AutoPlayWindowCapture_KeepsTopOfWindowAtTopOfImage()
        {
            yield return OpenProbeWindow();
            _outputPath = Path.Combine("Temp", $"uniforge_orientation_{System.Guid.NewGuid():N}.png");

            var captured = AutoPlayService.Instance.TryCaptureWindowInternal(_window, _outputPath, out var width, out var height);

            Assert.IsTrue(captured, "window capture failed");
            var image = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(image.LoadImage(File.ReadAllBytes(_outputPath)));
                Assert.AreEqual(width, image.width);
                Assert.AreEqual(height, image.height);
                AssertTopRedBottomBlue(image);
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        [UnityTest]
        public IEnumerator GrabPixels_KeepsTopOfWindowAtTopOfImage()
        {
            yield return OpenProbeWindow();

            var captured = EditorWindowCapture.TryGrabPixels(_window, out var pixels, out var width, out var height);

            Assert.IsTrue(captured, "window capture failed");
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                image.SetPixels(pixels);
                AssertTopRedBottomBlue(image);
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        private IEnumerator OpenProbeWindow()
        {
            _window = ScriptableObject.CreateInstance<OrientationProbeWindow>();
            _window.position = new Rect(100, 100, 320, 240);
            _window.ShowUtility();
            _window.Repaint();

            var timeoutAt = EditorApplication.timeSinceStartup + 5.0;
            while (_window.PaintCount < 2 && EditorApplication.timeSinceStartup < timeoutAt)
            {
                _window.Repaint();
                yield return null;
            }

            Assert.GreaterOrEqual(_window.PaintCount, 2, "probe window was not painted");
        }

        /// <summary>Texture2D の y は下から数えるので、上の 1/4 は y が大きい側</summary>
        private static void AssertTopRedBottomBlue(Texture2D image)
        {
            var x = image.width / 2;
            var top = image.GetPixel(x, image.height * 3 / 4);
            var bottom = image.GetPixel(x, image.height / 4);

            Assert.That(top.r, Is.GreaterThan(0.8f), $"top of image should be red, was {top}");
            Assert.That(top.b, Is.LessThan(0.2f), $"top of image should be red, was {top}");
            Assert.That(bottom.b, Is.GreaterThan(0.8f), $"bottom of image should be blue, was {bottom}");
            Assert.That(bottom.r, Is.LessThan(0.2f), $"bottom of image should be blue, was {bottom}");
        }
    }
}
