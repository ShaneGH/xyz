module ShinyHttpCache.Tests.CacheWriteTests

open System.Net.Http.Headers
open ShinyHttpCace.Utils
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
let ``Client request, With max-age, adds to cache, verify content only`` () =
        
    // arrange
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 33
        
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
        
        do! (Mock.Mock.verifyPutAsync (fun (_, _, values) -> async {
            let! content = values.GetRawContent()
            CollectionAssert.AreEqual([|33|], content)
            return true
        }) mocks
        |> Infra.Async.map (assertEqual 1))
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With max-age, adds to cache`` () =
        
    // arrange
    let (_, state, expectedResponse) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
        
    expectedResponse.Headers.CacheControl <- CacheControlHeaderValue()
    expectedResponse.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (key, _, values) ->
            Assert.AreEqual("G$:$:http://www.com/", key)
            
            match values.CacheSettings.ExpirySettings with
            | Some x ->
                assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) x.MustRevalidateAtUtc
                match x.Validator with
                | Validator.ExpirationDateUtc d ->
                    Assert.AreEqual (x.MustRevalidateAtUtc, d)
                | _ -> Assert.Fail()
            | _ -> Assert.Fail()
            
            true
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With expires, adds to cache`` () =
        
    // arrange
    let (_, state, expectedResponse) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
        
    expectedResponse.Headers.CacheControl <- CacheControlHeaderValue()
    expectedResponse.Content.Headers.Expires <- DateTimeOffset.UtcNow.AddDays(1.0) |> asNullable

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (key, _, values) ->
            Assert.AreEqual("G$:$:http://www.com/", key)
            
            match values.CacheSettings.ExpirySettings with
            | Some x ->
                assertDateAlmost expectedResponse.Content.Headers.Expires.Value.UtcDateTime x.MustRevalidateAtUtc
                match x.Validator with
                | Validator.ExpirationDateUtc d ->
                    Assert.AreEqual (x.MustRevalidateAtUtc, d)
                | _ -> Assert.Fail()
            | _ -> Assert.Fail()
            
            true
        ) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With expires in the past, still caches`` () =
        
    // arrange
    let (_, state, expectedResponse) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
        
    expectedResponse.Headers.CacheControl <- CacheControlHeaderValue()
    expectedResponse.Content.Headers.Expires <- DateTimeOffset.UtcNow.AddDays(-1.0) |> asNullable

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun _ -> true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask

[<Test>]
let ``Client request, With no headers or cache, checks cache first`` () =
        
    // arrange
    let (_, state, expectedResponse) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyGet (fun _ -> true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask