module ShinyHttpCache.Tests.Utils.AssertUtils
open System.Net.Http
open NUnit.Framework

let private asyncMap f x = async {
    let! x' = x
    return (f x')
} 

let assertResponse expectedContent (response: HttpResponseMessage) =
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