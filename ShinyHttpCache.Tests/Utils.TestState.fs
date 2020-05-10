module ShinyHttpCache.Tests.Utils.TestState
open System.Globalization
open System.Net.Http.Headers
open ShinyHttpCache.Tests.Utils.Mock
open System.Collections.Generic
open System.IO
open System.Threading
open ShinyHttpCache
open ShinyHttpCache.Model.CacheSettings
open ShinyHttpCache.Serialization.Dtos.V1
open System
open System.Net.Http
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils

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
    let removeId (_, x) = x
    
    Mock.newMock true
    |> Mock.get (fun _ -> true) (fun _ -> asyncRetn None)
    |> removeId
    |> Mock.put (fun _ -> true) (fun _ -> asyncRetn ())
    |> removeId
    |> Mock.buildUserKey (fun _ -> true) getUserKey
    |> removeId
    
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
        
let addHttpRequest args (state: ICachingHttpClientDependenciesMethods)=
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
        
    Mock.send (fun (req, _) -> req.RequestUri = Uri(args.url)) returnResponse state

module CachedData =
    type Args =
        {
            url: string
            user: string option
            addRequestContent: byte option
            addResponseContent: byte option
            method: HttpMethod
            expiry: DateTime option
            etag: (string * bool) option
            maxAge: TimeSpan option
            customHeaders: KeyValuePair<string, string[]> list   
        }
        
    type CacheUntil =
        | Expires of DateTime
        | Etag of (string * bool)
        | MaxAge of TimeSpan
        
    let value (cachedUntil: CacheUntil) =
        let (expires, etag, maxAge) =
            match cachedUntil with
            | Expires x -> (Some x, None, None)
            | Etag x -> (None, Some x, None)
            | MaxAge x -> (None, None, Some x)
        
        {
            url = "http://www.com"
            user = None
            addRequestContent = None
            addResponseContent = None
            method = HttpMethod.Get
            expiry = expires
            etag = etag
            maxAge = maxAge
            customHeaders = []
        }
        
    let setUrl url (x: Args) = { x with url = url  }
    let setUser user (x: Args) = { x with user = Some user  }
    let setResponseContent (responseContent: int) (x: Args) = { x with addResponseContent = byte responseContent |> Some  }
    let setRequestContent (requestContent: int) (x: Args) = { x with addRequestContent = byte requestContent |> Some  }
    let setMethod method x = { x with method = method  }
    let setExpiry expiry x = { x with expiry = expiry  }
    let setEtag etag x = { x with etag = Some etag  }
    let setMaxAge maxAge x = { x with maxAge = Some maxAge  }
    let addCustomHeader customHeader x = { x with customHeaders = customHeader :: x.customHeaders  }
open CachedData

let ignoreId (_, resp) = resp
    
let addToCache args (state: ICachingHttpClientDependenciesMethods) =
    
    let response = new HttpResponseMessage()
    response.RequestMessage <- new HttpRequestMessage()
    
    match args.addRequestContent with
    | Some x -> response.RequestMessage.Content <- new SingleByteContent.SingleByteContent(x)
    | None -> ()
    
    match args.addResponseContent with
    | Some x -> response.Content <- new SingleByteContent.SingleByteContent(x)
    | None -> response.Content <- new SingleByteContent.NoContent()
    
    match args.expiry with
    | Some x -> response.Content.Headers.Expires <- Nullable<DateTimeOffset> (DateTimeOffset(x))
    | None -> ()
    
    match args.etag with
    | Some (x, y) -> response.Headers.ETag <- EntityTagHeaderValue (x, y)
    | None -> ()
    
    match args.maxAge with
    | Some x -> response.Headers.CacheControl.MaxAge <- Nullable<TimeSpan> x
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
        
    Mock.get (fun x -> x = key) (fun _ -> resp) state