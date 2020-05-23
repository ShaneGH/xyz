module ShinyHttpCache.Tests.CacheWriteTests

open System.Net.Http.Headers
open ShinyHttpCache.Model
open ShinyHttpCache.Tests.Utils.AssertUtils
open System
open NUnit.Framework
open ShinyHttpCache.Tests.Utils
open ShinyHttpCache.Tests.Utils.TestUtils

[<Test>]
let ``Client request, With no headers or cache, avoids cache`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let (_, state, _) =
        TestState.build()
        |> TestState.mockHttpRequest req

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! response = response
        do! assertContent 2 response
        
        Mock.Mock.verifyPut (fun _ -> true) mocks
        |> assertEqual 0
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With max-age, adds to cache`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
        
    let (_, state, expectedResponse) =
        TestState.build()
        |> TestState.mockHttpRequest req
        
    expectedResponse.Headers.CacheControl <- CacheControlHeaderValue()
    expectedResponse.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (key, values, _) ->
            Assert.AreEqual("G$:$:http://www.com/", key)
            assertContent 2 values.
//                CustomAssert.AssertCachedResponse(1, input.Item2.HttpResponse);
//                CustomAssert.AssertDateAlmost(
//                    DateTime.UtcNow.AddDays(1), 
//                    ((Headers.CacheSettings.ExpirySettings.HardUtc)input.Item2.CacheSettings.ExpirySettings).Item);
//                return true;
            true
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
//
//        [Test]
//        public async Task ClientRequest_WithMaxAge_AddsToCache()
//        {
//            // arrange
//            var state = new TestState();
//            var expectedResponse = state.AddHttpRequest(1);
//            expectedResponse.Headers.CacheControl = new CacheControlHeaderValue
//            {
//                MaxAge = TimeSpan.FromDays(1)
//            };
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
//            state.Dependencies
//                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);
//
//            bool AssertResult(Tuple<string, CachedValues> input)
//            {
//                Assert.AreEqual("G$:$:http://www.com/", input.Item1);
//                CustomAssert.AssertCachedResponse(1, input.Item2.HttpResponse);
//                CustomAssert.AssertDateAlmost(
//                    DateTime.UtcNow.AddDays(1), 
//                    ((Headers.CacheSettings.ExpirySettings.HardUtc)input.Item2.CacheSettings.ExpirySettings).Item);
//                return true;
//            }
//        }
//
//        [Test]
//        public async Task ClientRequest_WithExpires_AddsToCache()
//        {
//            // arrange
//            var state = new TestState();
//            var expectedResponse = state.AddHttpRequest(1);
//            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(1);
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            Predicate<Tuple<string, CachedValues>> assert = AssertResult;
//            state.Dependencies
//                .Verify(x => x.Cache.Put(Match.Create(assert)), Times.Once);
//
//            bool AssertResult(Tuple<string, CachedValues> input)
//            {
//                CustomAssert.AssertDateAlmost(
//                    expectedResponse.Content.Headers.Expires.Value.UtcDateTime, 
//                    ((Headers.CacheSettings.ExpirySettings.Soft)input.Item2.CacheSettings.ExpirySettings).Item.MustRevalidateAtUtc);
//
//                return true;
//            }
//        }
//
//        [Test]
//        public async Task ClientRequest_WithExpiresInThePast_StillCaches()
//        {
//            // arrange
//            var state = new TestState();
//            var expectedResponse = state.AddHttpRequest(1);
//            expectedResponse.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            state.Dependencies
//                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, CachedValues>>()), Times.Once);
//        }
//        
//        [Test]
//        public async Task ClientRequest_WithNoHeadersOrCache_ChecksCacheFirst()
//        {
//            // arrange
//            var state = new TestState();
//            state.AddHttpRequest(1);
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            Predicate<string> assert = AssertResult;
//            state.Dependencies
//                .Verify(x => x.Cache.Get(It.IsAny<string>()), Times.Once);
//            state.Dependencies
//                .Verify(x => x.Cache.Get(Match.Create(assert)), Times.Once);
//
//            bool AssertResult(string input)
//            {
//                Assert.AreEqual("G$:$:http://www.com/", input);
//                return true;
//            }
//        }