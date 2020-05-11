module ShinyHttpCache.Tests.Utils.AssertUtils
open System
open System.Net.Http
open NUnit.Framework
open Newtonsoft.Json.Converters

let private asyncMap f x = async {
    let! x' = x
    return (f x')
} 

let assertEqual expected actual = Assert.AreEqual(expected, actual)

let assertContent expectedContent (response: HttpResponseMessage) =
    async {
        let! content = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
        CollectionAssert.AreEqual([| expectedContent |], content)
        return ()
    }

let assertValue (opt: 'a option) =
    match opt with
    | Some x -> x
    | None ->
        Assert.Fail("Expected option to have value")
        Unchecked.defaultof<'a>

let assertValueAsync x = asyncMap assertValue x

/// <summary>Assert that 2 date times are within 5 seconds of each other</summary>
let assertDateAlmost (expected: DateTime) (actual: DateTime) =
    let time = expected - actual
    let time =
        match time with
        | x when time < TimeSpan.Zero -> TimeSpan.Zero - x
        | x -> x

    Assert.Less(time, TimeSpan.FromSeconds(5.0));