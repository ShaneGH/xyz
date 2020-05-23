module ShinyHttpCache.Tests.UserCacheTests

open System.Net.Http.Headers
open ShinyHttpCache.Model
open ShinyHttpCache.Model.CacheSettings
open ShinyHttpCache.Tests.Utils.AssertUtils
open System
open NUnit.Framework
open ShinyHttpCache.Tests.Utils
open ShinyHttpCache.Tests.Utils.TestState
open ShinyHttpCache.Tests.Utils.TestUtils

[<Test>]
let ``Client request; with user header, max-age, public; Adds to shared cache`` () =
        
    // arrange
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
        
    response.Headers.CacheControl <- CacheControlHeaderValue()
    response.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable
    response.Headers.CacheControl.Private <- false

    // act
    let (mocks, response) =
        TestUtils.ExecuteRequestArgs.value
        |> TestUtils.ExecuteRequestArgs.setUser "my user"
        |> TestUtils.executeRequest
        <| state

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (key, _, _) ->
            Assert.AreEqual("G$:$:http://www.com/", key);
            true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request; with user header, max-age, private; Adds to user cache`` () =
        
    // arrange
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
        
    response.Headers.CacheControl <- CacheControlHeaderValue()
    response.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable
    response.Headers.CacheControl.Private <- true

    // act
    let (mocks, response) =
        TestUtils.ExecuteRequestArgs.value
        |> TestUtils.ExecuteRequestArgs.setUser "my user"
        |> TestUtils.executeRequest
        <| state

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (key, _, _) ->
            Assert.AreEqual("G$:my user$:http://www.com/", key);
            true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request; user name has $; Escapes dollar`` () =
        
    // arrange
    let (_, state, response) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
        
    response.Headers.CacheControl <- CacheControlHeaderValue()
    response.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable
    response.Headers.CacheControl.Private <- true

    // act
    let (mocks, response) =
        TestUtils.ExecuteRequestArgs.value
        |> TestUtils.ExecuteRequestArgs.setUser "my$user"
        |> TestUtils.executeRequest
        <| state

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyPut (fun (key, _, _) ->
            Assert.AreEqual("G$:my$$user$:http://www.com/", key);
            true) mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request; No headers or cache; Checks user and shared cache`` () =
        
    // arrange
    let (_, state, _) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value

    // act
    let (mocks, response) =
        TestUtils.ExecuteRequestArgs.value
        |> TestUtils.ExecuteRequestArgs.setUser "my user"
        |> TestUtils.executeRequest
        <| state

    // assert
    async {
        let! _ = response
        
        Mock.Mock.verifyGet (fun _ -> true) mocks
        |> assertEqual 2
        Mock.Mock.verifyGet ((=) "G$:my user$:http://www.com/") mocks
        |> assertEqual 1
        Mock.Mock.verifyGet ((=) "G$:$:http://www.com/") mocks
        |> assertEqual 1
        
        return ()
    } |> TestUtils.asTask
    
[<Test>]
let ``Client request; Cached value for user and shared; Returns user value`` () =
        
    // arrange
    let cached1 =
        TestState.CachedData.value (DateTime.UtcNow.AddDays(1.0) |> CachedData.Expires)
        |> TestState.CachedData.setResponseContent 99
    let cached2 =
        TestState.CachedData.value (DateTime.UtcNow.AddDays(1.0) |> CachedData.Expires)
        |> TestState.CachedData.setResponseContent 77
        |> TestState.CachedData.setUser "my user"
        
    let (_, state, _) =
        TestState.build()
        |> TestState.mockHttpRequest TestState.HttpRequestMock.value
    let (_, state) = TestState.addToCache cached1 state
    let (_, state) = TestState.addToCache cached2 state

    // act
    let (mocks, response) =
        TestUtils.ExecuteRequestArgs.value
        |> TestUtils.ExecuteRequestArgs.setUser "my user"
        |> TestUtils.executeRequest
        <| state

    // assert
    async {
        let! response = response
        do! assertContent 77 response
        
        return ()
    } |> TestUtils.asTask