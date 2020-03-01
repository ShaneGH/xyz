using Moq;
using NUnit.Framework;
using ShinyHttpCache.Tests.TestUtils;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using ShinyHttpCache.FSharp;
using ShinyHttpCache.Serialization;
using static ShinyHttpCache.FSharp.CachingHttpClient;

namespace ShinyHttpCache.Tests.FullRequestTests
{
    public class UserCacheTests
    {
        [Test]
        public async Task ClientRequest_WithUserHeaderMaxAgeAndPublic_AddsToSharedCache()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromDays(1),
                Private = false
            };

            // act
            var response = await state.ExecuteRequest(user: "my user");

            // assert
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                Assert.AreEqual("G$:$:http://www.com/", input.Item1);
                return true;
            }
        }

        [Test]
        public async Task ClientRequest_WithUserHeaderMaxAgeAndPrivate_AddsToUserCache()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromDays(1),
                Private = true
            };

            // act
            var response = await state.ExecuteRequest(user: "my user");

            // assert
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                Assert.AreEqual("G$:my user$:http://www.com/", input.Item1);
                return true;
            }
        }

        [Test]
        public async Task ClientRequest_UsernameHasDollar_EscapesDollarInKey()
        {
            // arrange
            var state = new TestState();
            var expectedResponse = state.AddHttpRequest(1);
            expectedResponse.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromDays(1),
                Private = true
            };

            // act
            var response = await state.ExecuteRequest(user: "my$user");

            // assert
            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
            state.Dependencies
                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);

            bool AssertResult(Tuple<string, CachedValues> input)
            {
                Assert.AreEqual("G$:my$$user$:http://www.com/", input.Item1);
                return true;
            }
        }
        
        [Test]
        public async Task ClientRequest_WithNoHeadersOrCache_ChecksUserAndSharedCache()
        {
            // arrange
            var state = new TestState();
            state.AddHttpRequest(1);

            // act
            var response = await state.ExecuteRequest(user: "my user");

            // assert
            Predicate<string> assertShared = AssertShared;
            Predicate<string> assertUser = AssertUser;
            state.Dependencies
                .Verify(x => x.Cache.Get(It.IsAny<string>()), Times.Exactly(2));
            state.Dependencies
                .Verify(x => x.Cache.Get(Match.Create(assertShared)), Times.Once);
            state.Dependencies
                .Verify(x => x.Cache.Get(Match.Create(assertUser)), Times.Once);

            bool AssertShared(string input)
            {
                return "G$:$:http://www.com/" == input;
            }

            bool AssertUser(string input)
            {
                return "G$:my user$:http://www.com/" == input;
            }
        }

        [Test]
        public async Task ClientRequest_CachedValueForUserAndShared_ReturnsUserValue()
        {
            // arrange
            var state = new TestState();
            var sharedResponse = state.AddToCache(DateTime.UtcNow.AddDays(1), addResponseContent: 1);
            var userResponse = state.AddToCache(DateTime.UtcNow.AddDays(1), addResponseContent: 2, user: "my user");
            var serverResponse = state.AddHttpRequest(3);

            // act
            var response = await state.ExecuteRequest(user: "my user");

            // assert
            await CustomAssert.AssertResponse(2, response);
        }
    }
}