using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    public class ParamBinderTests
    {
        private static JObject Schema()
        {
            return JObject.Parse(@"{
                'type': 'object',
                'required': ['side', 'trialCount'],
                'properties': {
                    'side': { 'enum': ['left','right','alternate','both'], 'default': 'right' },
                    'trialCount': { 'type': 'integer', 'minimum': 3, 'maximum': 100, 'default': 20 },
                    'reachPercent': { 'type': 'array', 'items': { 'type': 'number', 'minimum': 20, 'maximum': 110 }, 'minItems': 2, 'maxItems': 2, 'default': [50, 85] },
                    'adaptive': { 'type': 'boolean', 'default': false }
                }
            }".Replace('\'', '"'));
        }

        [Test]
        public void MissingOptionalParams_FillDefaults()
        {
            var result = ParamBinder.Bind(Schema(), new JObject());
            Assert.IsTrue(result.IsValid, string.Join("; ", result.Errors));
            Assert.AreEqual("right", result.Params.GetString("side"));
            Assert.AreEqual(20, result.Params.GetInt("trialCount"));
            CollectionAssert.AreEqual(new[] { 50.0, 85.0 }, result.Params.GetDoubleArray("reachPercent"));
            Assert.AreEqual(false, result.Params.GetBool("adaptive"));
        }

        [Test]
        public void ExplicitValues_OverrideDefaults()
        {
            var input = JObject.Parse(@"{ 'side': 'left', 'trialCount': 40 }".Replace('\'', '"'));
            var result = ParamBinder.Bind(Schema(), input);
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual("left", result.Params.GetString("side"));
            Assert.AreEqual(40, result.Params.GetInt("trialCount"));
        }

        [Test]
        public void InvalidEnum_ProducesError()
        {
            var input = JObject.Parse(@"{ 'side': 'up' }".Replace('\'', '"'));
            var result = ParamBinder.Bind(Schema(), input);
            Assert.IsFalse(result.IsValid);
            Assert.That(result.Errors, Has.Some.Contains("side"));
        }

        [Test]
        public void OutOfRangeInteger_ProducesError()
        {
            var input = JObject.Parse(@"{ 'trialCount': 500 }".Replace('\'', '"'));
            var result = ParamBinder.Bind(Schema(), input);
            Assert.IsFalse(result.IsValid);
            Assert.That(result.Errors, Has.Some.Contains("maximum"));
        }

        [Test]
        public void WrongArrayLength_ProducesError()
        {
            var input = JObject.Parse(@"{ 'reachPercent': [10, 20, 30] }".Replace('\'', '"'));
            var result = ParamBinder.Bind(Schema(), input);
            Assert.IsFalse(result.IsValid);
            Assert.That(result.Errors, Has.Some.Contains("maxItems"));
        }

        [Test]
        public void UnknownParam_ProducesError()
        {
            var input = JObject.Parse(@"{ 'wobble': 1 }".Replace('\'', '"'));
            var result = ParamBinder.Bind(Schema(), input);
            Assert.IsFalse(result.IsValid);
            Assert.That(result.Errors, Has.Some.Contains("unknown param 'wobble'"));
        }

        [Test]
        public void MissingRequiredWithNoDefault_ProducesError()
        {
            var schema = JObject.Parse(@"{
                'type': 'object',
                'required': ['side'],
                'properties': { 'side': { 'type': 'string' } }
            }".Replace('\'', '"'));
            var result = ParamBinder.Bind(schema, new JObject());
            Assert.IsFalse(result.IsValid);
            Assert.That(result.Errors, Has.Some.Contains("missing required"));
        }
    }
}
