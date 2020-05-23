module ShinyHttpCache.Tests.ModelTests

open System.Net.Http
open System.Net.Http.Headers
open ShinyHttpCache.Model
open ShinyHttpCache.Tests.Utils
open ShinyHttpCache.Tests.Utils.AssertUtils
open System
open NUnit.Framework
open ShinyHttpCache.Tests.Utils.TestUtils

let shouldCache () =
    let message = new HttpResponseMessage()
    message.Content <- new CustomContent.NoContent()
    message.Content.Headers.Expires <- DateTimeOffset.UtcNow.AddDays(1.0) |> asNullable
    message

[<Test>]
let ``buildCacheSettings, with no cache headers, returns none`` () =
        
    // arrange
    let message = new HttpResponseMessage()

    // act
    // assert
    CacheSettings.buildCacheSettings message
    |> assertNone

[<Test>]
let ``buildCacheSettings, with basic cache headers, returns some`` () =
        
    // arrange
    let message = shouldCache()

    // act
    // assert
    CacheSettings.buildCacheSettings message
    |> assertSome
    |> ignore

[<Test>]
let ``buildCacheSettings, with basic cache headers and no-store, returns none`` () =
        
    // arrange
    let message = shouldCache()
    message.Headers.CacheControl <- CacheControlHeaderValue()
    message.Headers.CacheControl.NoStore <- true

    // act
    // assert
    CacheSettings.buildCacheSettings message
    |> assertNone

[<Test>]
let ``buildCacheSettings, with basic cache headers, sets cache to public`` () =
        
    // arrange
    let message = shouldCache()

    // act
    let result = CacheSettings.buildCacheSettings message |> assertSome
    
    // assert
    Assert.True result.SharedCache

[<Test>]
let ``buildCacheSettings, with public cache, sets cache to public`` () =
        
    // arrange
    let message = shouldCache()
    message.Headers.CacheControl <- CacheControlHeaderValue()
    message.Headers.CacheControl.Private <- false

    // act
    let result = CacheSettings.buildCacheSettings message |> assertSome
    
    // assert
    Assert.True result.SharedCache

[<Test>]
let ``buildCacheSettings, with private cache, sets cache to private`` () =
        
    // arrange
    let message = shouldCache()
    message.Headers.CacheControl <- CacheControlHeaderValue()
    message.Headers.CacheControl.Private <- true

    // act
    let result = CacheSettings.buildCacheSettings message |> assertSome
    
    // assert
    Assert.False result.SharedCache

[<Test>]
let ``buildCacheSettings, with strong etag, sets correctly`` () =

    // arrange
    let message = new HttpResponseMessage()
    message.Headers.ETag <- EntityTagHeaderValue("\"etg\"", false)

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.ETag x ->
        match x with
        | CacheSettings.Strong x -> Assert.AreEqual("\"etg\"", x)
        | _ -> Assert.Fail()
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with weak etag, sets correctly`` () =

    // arrange
    let message = new HttpResponseMessage()
    message.Headers.ETag <- EntityTagHeaderValue("\"etg\"", true)

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.ETag x ->
        match x with
        | CacheSettings.Weak x -> Assert.AreEqual("\"etg\"", x)
        | _ -> Assert.Fail()
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with strong etag and expires, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Content <- new CustomContent.NoContent()
    message.Content.Headers.Expires <- DateTimeOffset.UtcNow.AddDays(1.0) |> asNullable
    message.Headers.ETag <- EntityTagHeaderValue("\"etg\"", false)

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.Both (tag, exp) ->
        assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) exp
        
        match tag with
        | CacheSettings.Strong x -> Assert.AreEqual("\"etg\"", x)
        | _ -> Assert.Fail()
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with weak etag and expires, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Content <- new CustomContent.NoContent()
    message.Content.Headers.Expires <- DateTimeOffset.UtcNow.AddDays(1.0) |> asNullable
    message.Headers.ETag <- EntityTagHeaderValue("\"etg\"", true)

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.Both (tag, exp) ->
        assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) exp
        
        match tag with
        | CacheSettings.Weak x -> Assert.AreEqual("\"etg\"", x)
        | _ -> Assert.Fail()
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with strong etag and max-age, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Headers.CacheControl <- CacheControlHeaderValue()
    message.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable
    message.Headers.ETag <- EntityTagHeaderValue("\"etg\"", false)

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.Both (tag, exp) ->
        assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) exp
        
        match tag with
        | CacheSettings.Strong x -> Assert.AreEqual("\"etg\"", x)
        | _ -> Assert.Fail()
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with weak etag and max-age, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Headers.CacheControl <- CacheControlHeaderValue()
    message.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable
    message.Headers.ETag <- EntityTagHeaderValue("\"etg\"", true)

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.Both (tag, exp) ->
        assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) exp
        
        match tag with
        | CacheSettings.Weak x -> Assert.AreEqual("\"etg\"", x)
        | _ -> Assert.Fail()
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with expires, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Content <- new CustomContent.NoContent()
    message.Content.Headers.Expires <- DateTimeOffset.UtcNow.AddDays(1.0) |> asNullable

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.ExpirationDateUtc exp ->
        assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) exp
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with max-age, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Headers.CacheControl <- CacheControlHeaderValue()
    message.Headers.CacheControl.MaxAge <- TimeSpan.FromDays(1.0) |> asNullable

    // act
    let result =
        CacheSettings.buildCacheSettings message
        |> Option.bind (fun x -> x.ExpirySettings)
        |> assertSome
    
    // assert
    match result.Validator with
    | CacheSettings.ExpirationDateUtc exp ->
        assertDateAlmost (DateTime.UtcNow.AddDays(1.0)) exp
    | _ -> Assert.Fail()

[<Test>]
let ``buildCacheSettings, with immutable, sets correctly`` () =
        
    // arrange
    let message = new HttpResponseMessage()
    message.Headers.CacheControl <- CacheControlHeaderValue.Parse("immutable")

    // act
    // assert
    CacheSettings.buildCacheSettings message
    |> assertSome
    |> (fun x -> x.ExpirySettings)
    |> assertNone