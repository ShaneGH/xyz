module ShinyHttpCache.Tests.ModelTests

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
        |> TestState.CachedData.setResponseContent 77
    
    let state =
        TestState.build()
        |> TestState.addToCache cached
        |> TestState.ignoreId

    // act
    let (mocks, response) =
        state
        |> TestUtils.executeRequest TestUtils.ExecuteRequestArgs.value

    // assert
    async {
        let! response = response
        do! assertContent 77 response
        
        Mock.Mock.verifySend (fun _ -> true) mocks
        |> assertEqual 0
        
        return ()
    } |> TestUtils.asTask