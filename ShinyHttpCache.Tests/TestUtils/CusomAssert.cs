using NUnit.Framework;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ShinyHttpCache.Tests.TestUtils
{
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
            CollectionAssert.AreEqual(content, new [] { expectedContent });
        }

        public static void AssertCachedResponse(byte expectedContent, CachedResponse.CachedResponse response)
        {
            Assert.NotNull(response.Content.Value);
            CollectionAssert.AreEqual(response.Content.Value.Content, new [] { expectedContent });
        }
    }
}