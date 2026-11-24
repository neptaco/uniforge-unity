using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using NUnit.Framework;
using UniForge.Tools.Mutations;
using UnityEngine;

namespace UniForge.Tests
{
    public class EvalValueConverterTests
    {
        [Test]
        public void ConvertsNull()
        {
            var result = EvalValueConverter.Convert(null);
            Assert.That(result.value, Is.Null);
            Assert.That(result.type, Is.EqualTo("null"));
        }

        [TestCase(true)]
        [TestCase(42)]
        [TestCase(1.25)]
        [TestCase(9007199254740992L)]
        [TestCase("text")]
        public void PreservesJsonScalars(object value)
        {
            var result = EvalValueConverter.Convert(value);
            Assert.That(result.value, Is.EqualTo(value));
            Assert.That(result.type, Is.EqualTo(value.GetType().FullName));
        }

        [Test]
        public void FormatsLargeNumbersAsStringsWithoutLosingTheirOriginalType()
        {
            Assert.That(EvalValueConverter.Convert(9007199254740993L), Is.EqualTo(((object)"9007199254740993", "System.Int64")));
            Assert.That(EvalValueConverter.Convert(long.MinValue), Is.EqualTo(((object)"-9223372036854775808", "System.Int64")));
            Assert.That(EvalValueConverter.Convert(9007199254740993UL), Is.EqualTo(((object)"9007199254740993", "System.UInt64")));
            Assert.That(EvalValueConverter.Convert(-9007199254740992.1m), Is.EqualTo(((object)"-9007199254740992.1", "System.Decimal")));
        }

        [Test]
        public void ConvertsSmallDecimalsToStrings()
        {
            Assert.That(EvalValueConverter.Convert(1.25m), Is.EqualTo(((object)"1.25", "System.Decimal")));
        }

        [Test]
        public void TruncatesStringsThatExceed65536Characters()
        {
            var limit = new string('a', 65536);
            Assert.That(EvalValueConverter.Convert(limit).value, Is.EqualTo(limit));
            var input = new[] { limit + "bc" };
            Assert.That(EvalValueConverter.Convert(input).value,
                Is.EqualTo(new[] { limit + "… (truncated, 65538 chars)" }));
        }

        [Test]
        public void CountsContainersAndNullsTowardSharedValueLimit()
        {
            var input = new OrderedDictionary();
            for (var index = 0; index < 11; index++)
                input.Add(index.ToString(), new object[100]);

            var output = (Dictionary<string, object>)EvalValueConverter.Convert(input).value;
            Assert.That(output, Has.Count.EqualTo(10));
            Assert.That(Enumerable.Range(0, 9).Select(index => ((IList)output[index.ToString()]).Count),
                Is.All.EqualTo(100));
            Assert.That((IList)output["9"], Has.Count.EqualTo(89));
            Assert.That(output.ContainsKey("10"), Is.False);
        }

        [Test]
        public void ConvertsEnumToItsName()
        {
            var result = EvalValueConverter.Convert(DayOfWeek.Monday);
            Assert.That(result.value, Is.EqualTo("Monday"));
            Assert.That(result.type, Is.EqualTo(typeof(DayOfWeek).FullName));
        }

        [Test]
        public void ConvertsUnityObjectAndTreatsDestroyedObjectAsNull()
        {
            var gameObject = new GameObject("Eval value");
            try
            {
                var result = EvalValueConverter.Convert(gameObject);
                Assert.That(result.type, Is.EqualTo(typeof(GameObject).FullName));
                Assert.That(result.value, Is.EqualTo(new Dictionary<string, object>
                {
                    ["type"] = typeof(GameObject).FullName,
                    ["name"] = gameObject.name,
                    ["instance_id"] = gameObject.GetInstanceID()
                }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            var destroyed = EvalValueConverter.Convert(gameObject);
            Assert.That(destroyed.value, Is.Null);
            Assert.That(destroyed.type, Is.EqualTo("null"));
        }

        [Test]
        public void ConvertsDictionaryKeysAndArrayValues()
        {
            var input = new Hashtable { [5] = new[] { 1, 2 } };
            var result = (Dictionary<string, object>)EvalValueConverter.Convert(input).value;
            Assert.That(result.Keys, Is.EqualTo(new[] { "5" }));
            Assert.That(result["5"], Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void TruncatesCollectionsTo100Items()
        {
            var sequence = Enumerable.Range(0, 110).ToArray();
            var dictionary = sequence.ToDictionary(number => number, number => number);

            Assert.That(EvalValueConverter.Convert(sequence).value, Is.EqualTo(sequence.Take(100)));
            Assert.That((IDictionary)EvalValueConverter.Convert(dictionary).value, Has.Count.EqualTo(100));
        }

        [Test]
        public void ConvertsValuesBeyondDepthThreeToString()
        {
            var deepest = new object[] { 1 };
            var input = new object[] { new object[] { new object[] { deepest } } };

            Assert.That(EvalValueConverter.Convert(input).value,
                Is.EqualTo(new object[] { new object[] { new object[] { deepest.ToString() } } }));
        }
    }
}
