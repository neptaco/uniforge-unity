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
        private sealed class AsyncStepState
        {
            public bool Completed;
            public AutoPlayService.StepResult Result;
            public System.Exception Exception;
        }

        [UnityTest]
        public IEnumerator RunTests_PlayMode_SmokeTest()
        {
            Assert.IsTrue(Application.isPlaying);
            yield return null;
            Assert.Pass();
        }

        [UnityTest]
        public IEnumerator AutoPlay_TapUi_FindsDontDestroyOnLoadButtonWithoutFocusingEditor()
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
            Object.DontDestroyOnLoad(canvasObject);

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

                var resultByName = AutoPlayService.Instance.ExecuteStep(JsonObject.Parse(
                    "{\"action\":\"tap_ui\",\"name\":\"Button\"}"));

                Assert.IsTrue(resultByName.Success, resultByName.Error);
                Assert.AreEqual("EventSystem", resultByName.SimulatorType);
                Assert.AreEqual(2, clickCount);
            }
            finally
            {
                Object.Destroy(eventSystemObject);
                Object.Destroy(canvasObject);
            }
        }

        [UnityTest]
        public IEnumerator AutoPlay_WaitMs_CompletesWhenTimeScaleIsZero()
        {
            var previousTimeScale = Time.timeScale;
            var state = new AsyncStepState();

            try
            {
                Time.timeScale = 0f;
                ExecuteStepAsync(
                    JsonObject.Parse("{\"action\":\"wait\",\"ms\":80}"),
                    state);

                var timeoutAt = Time.realtimeSinceStartup + 2f;
                while (!state.Completed && Time.realtimeSinceStartup < timeoutAt)
                    yield return null;

                Assert.IsNull(state.Exception);
                Assert.IsTrue(state.Completed, "wait_ms stalled while timeScale was zero");
                Assert.IsTrue(state.Result.Success, state.Result.Error);
                Assert.AreEqual(80, state.Result.WaitedMs);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator AutoPlay_WaitForUiState_WaitsForUiCreatedLater()
        {
            var state = new AsyncStepState();
            GameObject target = null;

            try
            {
                ExecuteStepAsync(
                    JsonObject.Parse(
                        "{\"action\":\"wait_for_ui_state\",\"name\":\"MCP_Test_LateUi\",\"condition\":\"active\",\"timeout_ms\":1000,\"poll_interval_ms\":20}"),
                    state);

                yield return new WaitForSecondsRealtime(0.08f);
                target = new GameObject("MCP_Test_LateUi", typeof(RectTransform));

                var timeoutAt = Time.realtimeSinceStartup + 2f;
                while (!state.Completed && Time.realtimeSinceStartup < timeoutAt)
                    yield return null;

                Assert.IsNull(state.Exception);
                Assert.IsTrue(state.Completed, "wait_for_ui_state did not observe a later-created UI element");
                Assert.IsTrue(state.Result.Success, state.Result.Error);
                Assert.AreEqual("wait_for_ui_state", state.Result.Action);
            }
            finally
            {
                if (target != null)
                    Object.Destroy(target);
            }
        }

        [UnityTest]
        public IEnumerator AutoPlay_InputText_FiresValueChangedOnceAndSupportsSubmit()
        {
            var inputObject = new GameObject(
                "MCP_Test_InputField",
                typeof(RectTransform),
                typeof(InputField));
            var inputField = inputObject.GetComponent<InputField>();
            var valueChangedCount = 0;
            var endEditCount = 0;
            inputField.onValueChanged.AddListener(_ => valueChangedCount++);
            inputField.onEndEdit.AddListener(_ => endEditCount++);

            try
            {
                var result = AutoPlayService.Instance.ExecuteStep(JsonObject.Parse(
                    "{\"action\":\"input_text\",\"name\":\"MCP_Test_InputField\",\"text\":\"sample\",\"submit\":true}"));

                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual("sample", inputField.text);
                Assert.AreEqual(1, valueChangedCount);
                Assert.AreEqual(1, endEditCount);
                yield return null;
            }
            finally
            {
                Object.Destroy(inputObject);
            }
        }

        [UnityTest]
        public IEnumerator AutoPlay_NameTarget_FailsWhenAmbiguous()
        {
            var first = new GameObject("MCP_Test_DuplicateUi", typeof(RectTransform));
            var second = new GameObject("MCP_Test_DuplicateUi", typeof(RectTransform));

            try
            {
                var result = AutoPlayService.Instance.ExecuteStep(JsonObject.Parse(
                    "{\"action\":\"tap_ui\",\"name\":\"MCP_Test_DuplicateUi\"}"));

                Assert.IsFalse(result.Success);
                Assert.That(result.Error, Does.Contain("Multiple GameObjects"));
                Assert.That(result.Error, Does.Contain("path"));
                yield return null;
            }
            finally
            {
                Object.Destroy(first);
                Object.Destroy(second);
            }
        }

        private static async void ExecuteStepAsync(JsonObject args, AsyncStepState state)
        {
            try
            {
                state.Result = await AutoPlayService.Instance.ExecuteStepAsync(args);
            }
            catch (System.Exception ex)
            {
                state.Exception = ex;
            }
            finally
            {
                state.Completed = true;
            }
        }
    }
}
