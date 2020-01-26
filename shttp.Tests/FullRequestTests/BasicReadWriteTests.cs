using Moq;
using NUnit.Framework;
using shttp.Tests.TestUtils;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace shttp.Tests
{
    public class BasicReadWriteTests
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
                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, HttpResponseMessage, DateTime>>()), Times.Never);
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
            Predicate<Tuple<string, HttpResponseMessage, DateTime>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, HttpResponseMessage, DateTime> input)
            {
                Assert.AreEqual("$:$:http://www.com/", input.Item1);
                Assert.AreEqual(response, input.Item2);
                CustomAssert.AssertDateAlmost(DateTime.UtcNow.AddDays(1), input.Item3);
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
            Predicate<Tuple<string, HttpResponseMessage, DateTime>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, HttpResponseMessage, DateTime> input)
            {
                CustomAssert.AssertDateAlmost(expectedResponse.Content.Headers.Expires.Value.UtcDateTime, input.Item3);
                return true;
            }
        }

        [Test]
        [Ignore("Not done yet")]
        public void ClientRequest_WithExpiresInThePastAndETag_Caches()
        {
        }

        [Test]
        public async Task ClientRequest_WithExpiresInThePast_DoesNotToCache()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);

            // act
            var response = await state.ExecuteRequest();

            // assert
            state.Dependencies
                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, HttpResponseMessage, DateTime>>()), Times.Never);
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
                Assert.AreEqual("$:$:http://www.com/", input);
                return true;
            }
        }

        [Test]
        public async Task ClientRequest_WithPreviouslyCachedValue_ReturnsCachedValue()
        {
            // arrange
            var state = new TestState();
            var cachedResponse = state.AddToCache(DateTime.UtcNow.AddDays(1), addResponseContent: 1);
            var serverResponse = state.AddHttpRequest(2);

            // act
            var response = await state.ExecuteRequest();

            // assert
            Assert.AreEqual(cachedResponse, response);
            state.Dependencies
                .Verify(x => x.Send(It.IsAny<Tuple<HttpRequestMessage, CancellationToken>>()), Times.Never);
        }
    }
}