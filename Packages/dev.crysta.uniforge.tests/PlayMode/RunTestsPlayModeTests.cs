using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UniForge.Services;

namespace UniForge.Tests
{
    public class RunTestsPlayModeTests
    {
        [UnityTest]
        public IEnumerator RunTests_PlayMode_SmokeTest()
        {
            Assert.IsTrue(Application.isPlaying);
            yield return null;
            Assert.Pass();
        }

        [UnityTest]
        public IEnumerator AutoPlay_TapUi_InvokesButtonThroughEventSystem()
        {
            var eventSystemObject = new GameObject("MCP_Test_EventSystem", typeof(EventSystem));
            var canvasObject = new GameObject("MCP_Test_Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var buttonObject = new GameObject(
                "Button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(canvasObject.transform, false);

            var clickCount = 0;
            buttonObject.GetComponent<Button>().onClick.AddListener(() => clickCount++);

            try
            {
                yield return null;

                Assert.IsNotNull(EventSystem.current);

                var result = AutoPlayService.Instance.ExecuteStep(JsonObject.Parse(
                    "{\"action\":\"tap_ui\",\"path\":\"MCP_Test_Canvas/Button\"}"));

                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual("EventSystem", result.SimulatorType);
                Assert.AreEqual(1, clickCount);
            }
            finally
            {
                Object.Destroy(eventSystemObject);
                Object.Destroy(canvasObject);
            }
        }
    }
}
