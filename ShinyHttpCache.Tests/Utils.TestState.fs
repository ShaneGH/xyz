module ShinyHttpCache.Tests.Utils.TestState
open System.Collections.Generic
open System.IO
open System.Threading
open ShinyHttpCache
open ShinyHttpCache.Model.CacheSettings
open ShinyHttpCache.Serialization.Dtos.V1
open System
open System.Net.Http
open Moq
open ShinyHttpCache.FSharp.CachingHttpClient
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils
open ShinyHttpCache.Utils.ReaderMonad

module Private =
    let asyncRetn x = async { return x }
open Private

let createPredicate (f: 'a -> bool) = Predicate<'a>(f)

let UserHeader = "x-test-user"

let private flatten = function
    | Some x -> x
    | None -> None
  
let private getUserKey(msg: CachedRequest) =
    match msg.Headers |> Seq.exists (fun x -> x.Key = UserHeader) with
    | false -> None
    | true ->
        msg.Headers
        |> Seq.filter (fun x -> x.Key = UserHeader)
        |> Seq.map (fun x -> Seq.tryHead x.Value)
        |> Seq.tryHead
        |> flatten
        
let build () =
    let dependencies = Mock<ICachingHttpClientDependencies>()
    
    let yy = asyncRetn<Stream option> None
    dependencies
        .Setup(fun x -> <@ x.Cache.Get(It.IsAny<string>()) @>)
        .Returns(yy) |> ignore

    dependencies
        .Setup(fun x -> <@ x.Cache.Put (It.IsAny<string>()) (It.IsAny<Unit>()) (It.IsAny<Stream>()) @>)
        .Returns(<@ asyncRetn () @>) |> ignore

    dependencies
        .Setup(fun x -> <@ x.Cache.Delete(It.IsAny<string>()) @>)
        .Returns(<@ asyncRetn () @>)  |> ignore

    dependencies
        .Setup(fun x -> <@ x.Cache.BuildUserKey(It.IsAny<CachedRequest>()) @>)
        .Returns(fun x -> <@ getUserKey x @>)  |> ignore

    dependencies
    
module HttpRequestMock =
    type Args =
        {
            addResponseContent: byte option
            url: string
            responseCode: int
        }
        
    let value =
        {
            addResponseContent = None
            url = "http://www.com"
            responseCode = 200
        }
        
    let setResponseContent (responseContent: int) (x: Args) = { x with addResponseContent = byte responseContent |> Some  }
    let setUrl url (x: Args) = { x with url = url  }   
    let setResponseCode responseCode x = { x with responseCode = responseCode  }
open HttpRequestMock
        
let addHttpRequest args (dependencies: Mock<ICachingHttpClientDependencies>)=
    let response = new HttpResponseMessage()
    match args.addResponseContent with
    | Some x -> response.Content <- new SingleByteContent.SingleByteContent(x) :> HttpContent
    | None -> ()

    let lck = obj()
    let mutable first = true;

    let returnResponse (msg: HttpRequestMessage, _: CancellationToken) =
        lock lck (fun _ ->
            match first with
            | true -> first <- false
            | false -> NotSupportedException() |> raise)

        response.RequestMessage <- msg;
        asyncRetn response

    let assertUrl =
        fun (msg: HttpRequestMessage, c) -> msg.RequestUri = Uri(args.url);
        |> createPredicate

    dependencies
        .Setup(fun x -> <@ x.Send(Match.Create(assertUrl)) @>)
        .Returns(fun x -> <@ returnResponse x @>)  |> ignore
    
    dependencies

module CachedData =
    type Args =
        {
            cachedUntil: DateTime
            url: string
            user: string option
            addRequestContent: byte option
            addResponseContent: byte option
            method: HttpMethod
            expiry: ExpirySettings
            customHeaders: KeyValuePair<string, string[]> list   
        }
        
    let value (cachedUntil: DateTime) =
        {
            cachedUntil = DateTime(cachedUntil.Ticks, DateTimeKind.Utc)
            url = "http://www.com"
            user = None
            addRequestContent = None
            addResponseContent = None
            method = HttpMethod.Get
            expiry = DateTime.UtcNow.AddDays(10.0) |> ExpirySettings.HardUtc
            customHeaders = []
        }
        
    let setCachedUntil (cachedUntil: DateTime) x = { x with cachedUntil = DateTime(cachedUntil.Ticks, DateTimeKind.Utc)  }
    let setUrl url (x: Args) = { x with url = url  }
    let setUser user (x: Args) = { x with user = Some user  }
    let setResponseContent (responseContent: int) (x: Args) = { x with addResponseContent = byte responseContent |> Some  }
    let setRequestContent (requestContent: int) (x: Args) = { x with addRequestContent = byte requestContent |> Some  }
    let setMethod method x = { x with method = method  }
    let setExpiry expiry x = { x with expiry = expiry  }
    let addCustomHeader customHeader x = { x with customHeaders = customHeader :: x.customHeaders  }
open CachedData

let addToCache args (dependencies: Mock<ICachingHttpClientDependencies>) =
    
    let response = new HttpResponseMessage()
    response.RequestMessage <- new HttpRequestMessage()
    
    match args.addRequestContent with
    | Some x -> response.RequestMessage.Content <- new SingleByteContent.SingleByteContent(x)
    | None -> ()
    
    match args.addResponseContent with
    | Some x -> response.Content <- new SingleByteContent.SingleByteContent(x)
    | None -> ()
    
    for x in args.customHeaders do
        response.Headers.Add(x.Key, x.Value)
        
    let method =
        match args.method with
        | x when x = HttpMethod.Get -> "G"
        | _ -> NotSupportedException(args.method.ToString()) |> raise
        
    let user =
        args.user
        |> Option.map (fun x -> x.Replace("$", "$$"))
        |> Option.defaultValue ""
        
    let key = sprintf "%s$:%s$:%A" method user (Uri(args.url))
    let resp = async {
        use! resp =
            match Model.build response with
            | Some x -> Serialization.Serializer.serialize x
            | None -> invalidOp "Expected Some"
            |> asyncMap (fun (_, x) -> x)
            
        // copy value so that original stream can be disposed of
        let respStr = Disposables.getValue resp
        let str = new MemoryStream() :> Stream
        respStr.CopyTo str
        str.Position <- 0L
        return Some str
    }
        
    dependencies
        .Setup(fun x -> <@ x.Cache.Get(key) @>)
        .Returns(fun _ -> <@ resp @>)  |> ignore
        
    dependencies