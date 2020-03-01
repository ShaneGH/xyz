using Microsoft.FSharp.Core;
using NUnit.Framework;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ShinyHttpCache.Serialization.HttpResponseMessage;

namespace ShinyHttpCache.Tests.TestUtils
{
    public class Jss : System.Text.Json.Serialization.JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }

    public static class CustomAssert
    {
        /// <summary>Assert that 2 date times are within 5 seconds of each other</summary>
        public static void AssertDateAlmost(DateTime expected, DateTime actual)
        {
            var time = expected - actual;
            if (time < TimeSpan.Zero)
                time *= -1;

            Assert.Less(time, TimeSpan.FromSeconds(5));
        }

        public static async Task AssertResponse(byte expectedContent, HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsByteArrayAsync();
            CollectionAssert.AreEqual(new[] { expectedContent }, content);
        }

        public static void AssertCachedResponse(byte expectedContent, CachedResponse.CachedResponse response)
        {
            Assert.NotNull(response.Content.Value);
            CollectionAssert.AreEqual(response.Content.Value.Content, new [] { expectedContent });
        }

        public static T IsSome<T>(FSharpOption<T> value)
        {
            Assert.True(FSharpOption<T>.get_IsSome(value));
            return value.Value;
        }

        public static void IsNone<T>(FSharpOption<T> value)
        {
            Assert.False(FSharpOption<T>.get_IsNone(value));
        }

        public static void Roughly(DateTime dt1, DateTime dt2)
        {
            Assert.Less(dt1, dt2.AddSeconds(2));
            Assert.Greater(dt1, dt2.AddSeconds(-2));
        }
    }
}