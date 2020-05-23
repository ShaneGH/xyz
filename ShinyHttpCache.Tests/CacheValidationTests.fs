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
        
        Mock.Mock.verifyPut (fun (_, _, settings) ->
            let assertExpiry () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
                    assertDateAlmost DateTime.UtcNow x.MustRevalidateAtUtc
                    true
                | _ -> false
                
            let assertEtag () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
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
        
        Mock.Mock.verifyPut (fun (_, _, settings) ->
            let assertExpiry () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
                    assertDateAlmost DateTime.UtcNow x.MustRevalidateAtUtc
                    true
                | _ -> false
                
            let assertEtag () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
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
        
        Mock.Mock.verifyPut (fun (_, _, settings) ->
                
            let assertExpiry () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
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
        
        Mock.Mock.verifyPut (fun (_, _, settings) ->
            let assertExpiry () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
                    // ensure that correct expires is used
                    assertDateAlmost response.Content.Headers.Expires.Value.UtcDateTime x.MustRevalidateAtUtc
                    true
                | _ -> false
                
            let assertEtag () =
                match settings.CacheSettings.ExpirySettings with
                | Some x ->
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
    
let setUpEtagScenario isWeak validationResponseWeakEtag =
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
    
    match validationResponseWeakEtag with
    | Some x -> expectedResponse.Headers.ETag <- EntityTagHeaderValue(x, true)
    | None -> ()
        
    let cached =
        TestState.CachedData.value (CachedData.Expires DateTime.MinValue)
        |> TestState.CachedData.setResponseContent 4
        |> TestState.CachedData.addCustomHeader "x-custom-header" "cached value"
        |> TestState.CachedData.setEtag "\"etg 1\"" isWeak
    
    TestState.addToCache cached state
    
[<Test>]
let ``Client request, With strong etag, server returns 304, constructs response correctly`` () =
    // arrange
    let (_, state) = setUpEtagScenario false None

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
    
[<Test>]
let ``Client request, With weak etag, server returns 304, constructs response correctly`` () =
    // arrange
    let (_, state) = setUpEtagScenario true None

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
        
        Assert.AreEqual("server value", r.Headers.GetValues("x-custom-header").First())
        
        Mock.Mock.verifySend (fun (req, _) ->
            Assert.AreEqual(@"""etg 1""", req.Headers.IfNoneMatch.First().Tag)
            Assert.True(req.Headers.IfNoneMatch.First().IsWeak)
            true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request, With weak eTag, Server returns another eTag, Caches correctly`` () =
    // arrange
    let (_, state) = setUpEtagScenario true (Some "\"new etag\"")

    // act
    let (mocks, r) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! r = r
        
        do! assertContent 4 r
        
        Mock.Mock.verifyPut (fun _ -> true) mocks
        |> assertEqual 1
        
        Assert.AreEqual("server value", r.Headers.GetValues("x-custom-header").First())
        
        Mock.Mock.verifyPut (fun (_, _, settings) ->
            match settings.CacheSettings.ExpirySettings with
            | Some x ->
                match x.Validator with
                | CacheSettings.ETag t
                | CacheSettings.Both (t, _) ->
                    match t with
                    | CacheSettings.Weak w -> Assert.AreEqual(@"""new etag""", w)
                    | _ -> Assert.Fail();
                | _ -> Assert.Fail();
            | _ -> Assert.Fail();
            true) mocks
        |> assertEqual 1
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request, With weak eTag, Server returns no eTag, does not re-cache`` () =
    // arrange
    let (_, state) = setUpEtagScenario true None

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
        
        Assert.AreEqual("server value", r.Headers.GetValues("x-custom-header").First())
        return ()
    } |> TestUtils.asTask