module ShinyHttpCache.Tests.CacheReadTests

open ShinyHttpCache.Tests.Utils.AssertUtils
open System
open NUnit.Framework
open ShinyHttpCache.Tests.Utils
open ShinyHttpCache.Tests.Utils.TestState

[<Test>]
let ``Client request, with previously cached value, returns cached value`` () =
        
    // arrange
    let cached =
        TestState.CachedData.value (DateTime.UtcNow.AddDays(1.0) |> CachedData.Expires)
        |> TestState.CachedData.setResponseContent 1
        
    let req =
        TestState.HttpRequestMock.value
        |> TestState.HttpRequestMock.setResponseContent 2
    
    let state =
        TestState.build()
        |> TestState.addToCache cached
        |> TestState.ignoreId
        |> TestState.addHttpRequest req
        |> TestState.ignoreId

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! response = response |> assertValueAsync
        do! assertResponse 1 response
        
//        state. .Verify(fun x -> <@ x.Send(It.IsAny<(HttpRequestMessage * CancellationToken)>()) @>, Times.Never)
//        |> ignore
        
        return ()
    } |> TestUtils.asTask
//    await CustomAssert.AssertResponse(1, response);
//    state.Dependencies
//        .Verify(x => x.Send(It.IsAny<Tuple<HttpRequestMessage, CancellationToken>>()), Times.Never);