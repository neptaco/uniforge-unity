using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
    }
}
