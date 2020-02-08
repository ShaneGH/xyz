using Moq;
using NUnit.Framework;
using ShinyHttpCache.Tests.TestUtils;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ShinyHttpCache.Tests.FullRequestTests
{
    public class ValidateRequestTests
    {
        [Test]
        public async Task ClientRequest_WithExpiresInThePastAndETag_Caches()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);
            expectedResponse.Headers.ETag = new EntityTagHeaderValue("\"etg\"", false);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachingHttpClient.CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachingHttpClient.CachedValues> input)
            {
                // if there is an ETag, expires should be now, and not the past expiry
                var settings = ((Headers.CacheSettings.ExpirySettings.Soft)input.Item2.CacheSettings.ExpirySettings).Item;
                CustomAssert.AssertDateAlmost(DateTime.UtcNow, settings.MustRevalidateAtUtc);

                var both = ((Headers.CacheSettings.Validator.Both)settings.Validator).Item;
                CustomAssert.AssertDateAlmost(
                    expectedResponse.Content.Headers.Expires.Value.UtcDateTime, 
                    both.Item2);

                var strong = (Headers.CacheSettings.EntityTag.Strong)both.Item1;
                Assert.AreEqual("\"etg\"", strong.Item);

                return true;
            }
        }
        
        [Test]
        public async Task ClientRequest_ExistingCach()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);
            expectedResponse.Headers.ETag = new EntityTagHeaderValue("\"etg\"", false);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachingHttpClient.CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachingHttpClient.CachedValues> input)
            {
                // if there is an ETag, expires should be now, and not the past expiry
                var settings = ((Headers.CacheSettings.ExpirySettings.Soft)input.Item2.CacheSettings.ExpirySettings).Item;
                CustomAssert.AssertDateAlmost(DateTime.UtcNow, settings.MustRevalidateAtUtc);

                var both = ((Headers.CacheSettings.Validator.Both)settings.Validator).Item;
                CustomAssert.AssertDateAlmost(
                    expectedResponse.Content.Headers.Expires.Value.UtcDateTime, 
                    both.Item2);

                var strong = (Headers.CacheSettings.EntityTag.Strong)both.Item1;
                Assert.AreEqual("\"etg\"", strong.Item);

                return true;
            }
        }
    }
}