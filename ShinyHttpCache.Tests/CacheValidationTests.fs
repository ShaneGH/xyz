module ShinyHttpCache.Tests.CacheValidationTests

open System.Net
open System.Net.Http.Headers
open ShinyHttpCache.Model
open ShinyHttpCache.Tests.Utils.AssertUtils
open System
open System.Linq
open NUnit.Framework
open ShinyHttpCache.Tests.Utils
open ShinyHttpCache.Tests.Utils.TestState

[<Test>]
let ``Client request, With strong eTag, caches correctly`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest req
    
    response.Headers.ETag <- EntityTagHeaderValue("\"etg\"", false);

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (_, settings, _) ->
            let assertExpiry () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    assertDateAlmost DateTime.UtcNow x.MustRevalidateAtUtc
                    true
                | _ -> false
                
            let assertEtag () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    match x.Validator with
                    | CacheSettings.ETag e ->
                        match e with
                        | CacheSettings.Strong t -> t = "\"etg\""
                        | _ -> false
                    | _ -> false
                | _ -> false
                
            assertExpiry() && assertEtag()
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With weak eTag, caches correctly`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest req
    
    response.Headers.ETag <- EntityTagHeaderValue("\"etg\"", true);

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (_, settings, _) ->
            let assertExpiry () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    assertDateAlmost DateTime.UtcNow x.MustRevalidateAtUtc
                    true
                | _ -> false
                
            let assertEtag () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    match x.Validator with
                    | CacheSettings.ETag e ->
                        match e with
                        | CacheSettings.Weak t -> t = "\"etg\""
                        | _ -> false
                    | _ -> false
                | _ -> false
                
            assertExpiry() && assertEtag()
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With expires, caches correctly`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest req
    
    response.Content.Headers.Expires <- Nullable<DateTimeOffset>(DateTimeOffset.UtcNow.AddDays(-1.0))

    // act
    let (mocks, r) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = r
        
        Mock.Mock.verifyPut (fun (_, settings, _) ->
                
            let assertExpiry () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    match x.Validator with
                    | CacheSettings.ExpirationDateUtc e ->
                        assertDateAlmost x.MustRevalidateAtUtc e
                        assertDateAlmost response.Content.Headers.Expires.Value.UtcDateTime e
                        true
                    | _ -> false
                | _ -> false
                
            assertExpiry()
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request, With expires in the past and eTag, caches correctly`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest req
    
    response.Headers.ETag <- EntityTagHeaderValue("\"etg\"", false)
    response.Content.Headers.Expires <- Nullable<DateTimeOffset>(DateTimeOffset.UtcNow.AddDays(-1.0))

    // act
    let (mocks, r) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = r
        
        Mock.Mock.verifyPut (fun (_, settings, _) ->
            let assertExpiry () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    // ensure that correct expires is used
                    assertDateAlmost response.Content.Headers.Expires.Value.UtcDateTime x.MustRevalidateAtUtc
                    true
                | _ -> false
                
            let assertEtag () =
                match settings.ExpirySettings with
                | CacheSettings.Soft x ->
                    match x.Validator with
                    | CacheSettings.Both (e, dt) ->
                        match e with
                        | CacheSettings.Strong t ->
                            assertDateAlmost response.Content.Headers.Expires.Value.UtcDateTime dt
                            t = "\"etg\""
                        | _ -> false
                    | _ -> false
                | _ -> false
                
            assertExpiry() && assertEtag()
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request, With strong etag, server returns 304, constructs response correctly`` () =
    // arrange
    let state = TestState.build()
    
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let (_, state, expectedResponse) =
        state
        |> TestState.mockHttpRequest req
    
    expectedResponse.StatusCode <- HttpStatusCode.NotModified
    expectedResponse.Headers.Add("x-custom-header", [|"server value"|])
        
    let cached =
        TestState.CachedData.value (CachedData.Expires DateTime.MinValue)
        |> TestState.CachedData.setResponseContent 4
        |> TestState.CachedData.addCustomHeader "x-custom-header" "cached value"
        |> TestState.CachedData.setEtag "\"etg 1\"" false
    
    let (_, state) = TestState.addToCache cached state

    // act
    let (mocks, r) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! r = r
        
        do! assertContent 4 r
        
        Mock.Mock.verifyPut (fun _ -> true) mocks
        |> assertEqual 0
        
        Assert.AreEqual("cached value", r.Headers.GetValues("x-custom-header").First())
        
        Mock.Mock.verifySend (fun (req, _) ->
            Assert.AreEqual(@"""etg 1""", req.Headers.IfNoneMatch.First().Tag)
            Assert.False(req.Headers.IfNoneMatch.First().IsWeak)
            true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
//
//        private static TestState SetUpWeakETagScenario(string secondResponseETag)
//        {
//            var state = new TestState();
//
//            state.AddToCache(
//                DateTime.MinValue, 
//                addResponseContent: 4, 
//                customHeaders: new[]{ KeyValuePair.Create("x-custom-header", new[]{ "cached value" }) },
//                expiry: NewSoft(new CacheSettings.RevalidationSettings(
//                    DateTime.MinValue, 
//                    CacheSettings.Validator.NewETag(CacheSettings.EntityTag.NewWeak("\"etg 1\"")))));
//
//            var expectedResponse = state.AddHttpRequest(null, responseCode: 304);
//            expectedResponse.Headers.Add("x-custom-header", new[]{ "server value" });
//            if (secondResponseETag != null)
//            {
//                expectedResponse.Headers.ETag = new EntityTagHeaderValue(secondResponseETag, true);
//            }
//
//            return state;
//        }
//           
//        [Test]
//        public async Task ClientRequest_WithWeakETag_ServerReturns304_ConstructsResponseCorrectly()
//        {
//            // arrange
//            var state = SetUpWeakETagScenario(null);
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            await CustomAssert.AssertResponse(4, response);
//            Assert.AreEqual("server value", response.Headers.GetValues("x-custom-header").First());
//                
//            Predicate<Tuple<HttpRequestMessage, CancellationToken>> assertHttpSend = AssertHttpSend;
//            state.Dependencies
//                .Verify(x => x.Send(Match.Create(assertHttpSend)), Times.Once);
//
//            bool AssertHttpSend(Tuple<HttpRequestMessage, CancellationToken> input)
//            {
//                Assert.AreEqual(@"""etg 1""", input.Item1.Headers.IfNoneMatch.First().Tag);
//                Assert.True(input.Item1.Headers.IfNoneMatch.First().IsWeak);
//                return true;
//            }
//        }
//           
//        [Test]
//        public async Task ClientRequest_WithWeakETag_ServerReturnsAnotherETag_CachesCorrectly()
//        {
//            // arrange
//            var state = SetUpWeakETagScenario("\"etg 2\"");
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            Predicate<Tuple<string, CachedValues>> assertCachePut = AssertCachePut;
//            state.Dependencies
//                .Verify(x => x.Cache.Put(Match.Create(assertCachePut)), Times.Once);
//
//            bool AssertCachePut(Tuple<string, CachedValues> input)
//            {
//                Assert.AreEqual("server value", response.Headers.GetValues("x-custom-header").First());
//
//                var settings = ((Soft)input.Item2.CacheSettings.ExpirySettings).Item;
//                CustomAssert.AssertDateAlmost(DateTime.UtcNow, settings.MustRevalidateAtUtc);
//
//                var etag = ((Headers.CacheSettings.Validator.ETag)settings.Validator).Item;
//                var weak = (Headers.CacheSettings.EntityTag.Weak)etag;
//                Assert.AreEqual("\"etg 2\"", weak.Item);
//
//                return true;
//            }
//        }
//           
//        [Test]
//        public async Task ClientRequest_WithWeakETag_ServerReturnsNoETag_DoesNotReCache()
//        {
//            // arrange
//            var state = SetUpWeakETagScenario(null);
//
//            // act
//            var response = await state.ExecuteRequest();
//
//            // assert
//            state.Dependencies
//                .Verify(x => x.Cache.Put(It.IsAny<Tuple<string, CachedValues>>()), Times.Never);
//        }