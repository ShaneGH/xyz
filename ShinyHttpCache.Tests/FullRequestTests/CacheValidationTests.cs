using Moq;
using NUnit.Framework;
using ShinyHttpCache.Tests.TestUtils;
using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using ShinyHttpCache.Headers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Net.Http;
using static ShinyHttpCache.Headers.CacheSettings.ExpirySettings;
using static ShinyHttpCache.FSharp.CachingHttpClient;

namespace ShinyHttpCache.Tests.FullRequestTests
{
    public class CacheValidationTests
    {   
        [Test]
        public async Task ClientRequest_WithStrongETag_Caches()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Headers.ETag = new EntityTagHeaderValue("\"etg\"", false);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                // if there is an ETag, expires should be now, and not the past expiry
                var settings = ((Soft)input.Item2.CacheSettings.ExpirySettings).Item;
                CustomAssert.AssertDateAlmost(DateTime.UtcNow, settings.MustRevalidateAtUtc);

                var etag = ((Headers.CacheSettings.Validator.ETag)settings.Validator).Item;
                var strong = (Headers.CacheSettings.EntityTag.Strong)etag;
                Assert.AreEqual("\"etg\"", strong.Item);

                return true;
            }
        }
           
        [Test]
        public async Task ClientRequest_WithWeakETag_Caches()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Headers.ETag = new EntityTagHeaderValue("\"etg\"", true);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                // if there is an ETag, expires should be now, and not the past expiry
                var settings = ((Soft)input.Item2.CacheSettings.ExpirySettings).Item;
                CustomAssert.AssertDateAlmost(DateTime.UtcNow, settings.MustRevalidateAtUtc);

                var etag = ((Headers.CacheSettings.Validator.ETag)settings.Validator).Item;
                var weak = (Headers.CacheSettings.EntityTag.Weak)etag;
                Assert.AreEqual("\"etg\"", weak.Item);

                return true;
            }
        }

        [Test]
        public async Task ClientRequest_WithExpires_Caches()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                var settings = ((Soft)input.Item2.CacheSettings.ExpirySettings).Item;
                var expirationDate = ((Headers.CacheSettings.Validator.ExpirationDateUtc)settings.Validator).Item;
                
                CustomAssert.AssertDateAlmost(expirationDate, settings.MustRevalidateAtUtc);

                CustomAssert.AssertDateAlmost(
                    expectedResponse.Content.Headers.Expires.Value.UtcDateTime, 
                    expirationDate);

                return true;
            }
        }

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
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                // if there is an ETag, expires should be now, and not the past expiry
                var settings = ((Soft)input.Item2.CacheSettings.ExpirySettings).Item;
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
        public async Task ClientRequest_WithStrongETag_ServerReturns304_ConstructsResponseCorrectly()
        {
            // arrange
            var state = new TestState();

            state.AddToCache(
                DateTime.MinValue, 
                addResponseContent: 4, 
                customHeaders: new[]{ KeyValuePair.Create("x-custom-header", new[]{ "cached value" }) },
                expiry: NewSoft(new CacheSettings.RevalidationSettings(
                    DateTime.MinValue, 
                    CacheSettings.Validator.NewETag(CacheSettings.EntityTag.NewStrong("\"etg 1\"")))));

            var expectedResponse = state.AddHttpRequest(null, responseCode: 304);
            expectedResponse.Headers.Add("x-custom-header", new[]{ "server value" });

            // act
            var response = await state.ExecuteRequest();

            // assert
            await CustomAssert.AssertResponse(4, response);
            state.Dependencies
                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, CachedValues>>()), Times.Never);
            Assert.AreEqual("cached value", response.Headers.GetValues("x-custom-header").First());
                
            Predicate<Tuple<HttpRequestMessage, CancellationToken>> assertHttpSend = AssertHttpSend;
            state.Dependencies
                .Verify(x => x.Send(Match.Create(assertHttpSend)), Times.Once);

            bool AssertHttpSend(Tuple<HttpRequestMessage, CancellationToken> input)
            {
                Assert.AreEqual(@"""etg 1""", input.Item1.Headers.IfNoneMatch.First().Tag);
                Assert.False(input.Item1.Headers.IfNoneMatch.First().IsWeak);
                return true;
            }
        }

        private static TestState SetUpWeakETagScenario(string secondResponseETag)
        {
            var state = new TestState();

            state.AddToCache(
                DateTime.MinValue, 
                addResponseContent: 4, 
                customHeaders: new[]{ KeyValuePair.Create("x-custom-header", new[]{ "cached value" }) },
                expiry: NewSoft(new CacheSettings.RevalidationSettings(
                    DateTime.MinValue, 
                    CacheSettings.Validator.NewETag(CacheSettings.EntityTag.NewWeak("\"etg 1\"")))));

            var expectedResponse = state.AddHttpRequest(null, responseCode: 304);
            expectedResponse.Headers.Add("x-custom-header", new[]{ "server value" });
            if (secondResponseETag != null)
            {
                expectedResponse.Headers.ETag = new EntityTagHeaderValue(secondResponseETag, true);
            }

            return state;
        }
           
        [Test]
        public async Task ClientRequest_WithWeakETag_ServerReturns304_ConstructsResponseCorrectly()
        {
            // arrange
            var state = SetUpWeakETagScenario(null);

            // act
            var response = await state.ExecuteRequest();

            // assert
            await CustomAssert.AssertResponse(4, response);
            Assert.AreEqual("server value", response.Headers.GetValues("x-custom-header").First());
                
            Predicate<Tuple<HttpRequestMessage, CancellationToken>> assertHttpSend = AssertHttpSend;
            state.Dependencies
                .Verify(x => x.Send(Match.Create(assertHttpSend)), Times.Once);

            bool AssertHttpSend(Tuple<HttpRequestMessage, CancellationToken> input)
            {
                Assert.AreEqual(@"""etg 1""", input.Item1.Headers.IfNoneMatch.First().Tag);
                Assert.True(input.Item1.Headers.IfNoneMatch.First().IsWeak);
                return true;
            }
        }
           
        [Test]
        public async Task ClientRequest_WithWeakETag_ServerReturnsAnotherETag_CachesCorrectly()
        {
            // arrange
            var state = SetUpWeakETagScenario("\"etg 2\"");

            // act
            var response = await state.ExecuteRequest();

            // assert
            Predicate<Tuple<string, CachedValues>> assertCachePut = AssertCachePut;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assertCachePut)), Times.Once);

            bool AssertCachePut(Tuple<string, CachedValues> input)
            {
                Assert.AreEqual("server value", response.Headers.GetValues("x-custom-header").First());

                var settings = ((Soft)input.Item2.CacheSettings.ExpirySettings).Item;
                CustomAssert.AssertDateAlmost(DateTime.UtcNow, settings.MustRevalidateAtUtc);

                var etag = ((Headers.CacheSettings.Validator.ETag)settings.Validator).Item;
                var weak = (Headers.CacheSettings.EntityTag.Weak)etag;
                Assert.AreEqual("\"etg 2\"", weak.Item);

                return true;
            }
        }
           
        [Test]
        public async Task ClientRequest_WithWeakETag_ServerReturnsNoETag_DoesNotReCache()
        {
            // arrange
            var state = SetUpWeakETagScenario(null);

            // act
            var response = await state.ExecuteRequest();

            // assert
            state.Dependencies
                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, CachedValues>>()), Times.Never);
        }
    }
}