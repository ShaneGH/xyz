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
    public class CacheWriteTests
    {
        [Test]
        public async Task ClientRequest_WithNoHeadersOrCache_AvoidsCache()
        {
            // arrange
            var state = new TestState();
            state.AddHttpRequest(1);

            // act
            var response = await state.ExecuteRequest();

            // assert
            await CustomAssert.AssertResponse(1, response);
            state.Dependencies
                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, CachingHttpClient.CachedValues>>()), Times.Never);
        }

        [Test]
        public async Task ClientRequest_WithMaxAge_AddsToCache()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromDays(1)
            };

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachingHttpClient.CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachingHttpClient.CachedValues> input)
            {
                Assert.AreEqual("G$:$:http://www.com/", input.Item1);
                CustomAssert.AssertCachedResponse(1, input.Item2.HttpResponse);
                CustomAssert.AssertDateAlmost(
                    DateTime.UtcNow.AddDays(1), 
                    ((Headers.CacheSettings.ExpirySettings.HardUtc)input.Item2.CacheSettings.ExpirySettings).Item);
                return true;
            }
        }

        [Test]
        public async Task ClientRequest_WithExpires_AddsToCache()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(1);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachingHttpClient.CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachingHttpClient.CachedValues> input)
            {
                CustomAssert.AssertDateAlmost(
                    expectedResponse.Content.Headers.Expires.Value.UtcDateTime, 
                    ((Headers.CacheSettings.ExpirySettings.Soft)input.Item2.CacheSettings.ExpirySettings).Item.MustRevalidateAtUtc);

                return true;
            }
        }

        [Test]
        public async Task ClientRequest_WithExpiresInThePast_StillCaches()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);

            // act
            var response = await state.ExecuteRequest();

            // assert
            state.Dependencies
                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, CachingHttpClient.CachedValues>>()), Times.Once);
        }
        
        [Test]
        public async Task ClientRequest_WithNoHeadersOrCache_ChecksCacheFirst()
        {
            // arrange
            var state = new TestState();
            state.AddHttpRequest(1);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<string> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Get(It.IsAny<string>()), Times.Once);
            state.Dependencies
                .Verify(x => x.Cache.Get(Match.Create(assert)), Times.Once);

            bool AssertResult(string input)
            {
                Assert.AreEqual("G$:$:http://www.com/", input);
                return true;
            }
        }
    }
}