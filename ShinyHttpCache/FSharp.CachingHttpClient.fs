module ShinyHttpCache.FSharp.CachingHttpClient

open System
open System.Net.Http.Headers
open System.Net.Http
open System.Threading
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils.ReaderMonad
open ShinyHttpCache.Model
open ShinyHttpCache.Model.CacheSettings
open System.Net
open System.IO
open ShinyHttpCache.Serialization;
open ShinyHttpCache.Model
open ShinyHttpCache.Utils

type ICache =
    abstract member Get : string -> Stream option Async
    //TODO: replace Unit with the unserialized version of the stream
    abstract member Put : (string * Unit * Stream) -> unit Async
    abstract member Delete : string -> unit Async
    abstract member BuildUserKey : CachedRequest -> string option

type ICachingHttpClientDependencies =
    abstract member Cache : ICache
    abstract member Send : (HttpRequestMessage * CancellationToken) -> HttpResponseMessage Async

// what codes are cachable
// what methods are cachable

// Cache-Control
//  private, max-age=0, no-cache
//  public no-store s-maxage must-revalidate proxy-revalidate no-transform
//  immutable stale-while-revalidate stale-if-error
// pragma (old)
// expires (old)
// etag
//  W/"asdasdasd"
// Last-Modified

module private Private =
    let private getMethodKey (method: HttpMethod) =
        match method with
        | x when x = HttpMethod.Get -> "G"
        // TODO, more methods (especially put with etag)
        | _ -> NotSupportedException "Only GET methods are supported for caching" |> raise
        
    let buildUserCacheKey method (uri: Uri) (userKey: string) =
        // TODO: include http method
        // TODO: allow custom key build method (e.g. to include headers)
        let userKey = if String.IsNullOrEmpty userKey then "" else userKey
        let m = getMethodKey method

        sprintf "%s$:%s$:%O"
        <| m
        <| userKey.Replace("$", "$$")
        <| uri

    let buildSharedCacheKey method (uri: Uri) = buildUserCacheKey method uri ""

    let traverseAsyncOpt input =
        async {
            match input with
            | Some x -> 
                let! x1 = x
                return Some x1
            | None -> return None 
        }

    let squashOptions = function
        | Some x -> 
            match x with
            | Some y -> Some y
            | None -> None
        | None -> None

    let asyncMap f x =
        async {
            let! x1 = x
            return f x1
        }

    let asyncBind f x =
        async {
            let! x1 = x
            return! f x1
        }

    let asyncRetn x = async { return x }

    let asyncUnit = asyncRetn ()

    let tryGetCacheResult req =
        let execute (cache: ICachingHttpClientDependencies) =
            let userKey = cache.Cache.BuildUserKey req

            let reqMethod = HttpMethod(req.Method)
            let userResult = 
                userKey 
                |> Option.map (buildUserCacheKey reqMethod req.Uri)
                |> Option.map cache.Cache.Get
                |> traverseAsyncOpt
                |> asyncMap squashOptions

            let sharedResult = 
                buildSharedCacheKey reqMethod req.Uri
                |> cache.Cache.Get

            async {
                let! usr = userResult
                let! shr = sharedResult

                match usr, shr with
                | Some x, _
                | _, Some x -> 
                    let! result = Versioned.deserialize x
                    return Some result
                | _ -> return None
            }

        Reader.Reader execute

    type HttpResponseType =
        | FromCache of CachedValues
        | FromServer of HttpResponseMessage
        // TODO: reduce the scope of the first arg to just the response
        // last arg is "StrongValidation"
        | Hybrid of (CachedValues * HttpResponseMessage * bool)

    let toEntityTagHeader = function
        | Strong x -> EntityTagHeaderValue(x, false)
        | Weak x -> EntityTagHeaderValue(x, true)

    let rec addValidationHeaders (request: HttpRequestMessage) = function
        | ETag x -> 
            toEntityTagHeader x
            |> request.Headers.IfNoneMatch.Add
        | ExpirationDateUtc x ->
            let expired = DateTimeOffset x |> Nullable<DateTimeOffset>
            request.Headers.IfModifiedSince <- expired
        | Both (x, y) -> 
            ETag x |> addValidationHeaders request
            ExpirationDateUtc y |> addValidationHeaders request

    type CacheBehavior =
        | Req of HttpRequestMessage
        | ReqWithValidation of (HttpRequestMessage * CachedValues * bool)
        | Resp of CachedValues

    let isStrongValidation = function
        | Both (x, _)
        | ETag x -> match x with | Strong _ -> true | _ -> false
        | ExpirationDateUtc _ -> false

    let sendHttpRequest (request, token) (cacheResult: CachedValues) =
        let cacheBehavior =
            match cacheResult.CacheSettings.ExpirySettings with
            | NoExpiryDate -> Resp cacheResult
            | HardUtc exp when exp > DateTime.UtcNow -> Resp cacheResult
            | Soft s when s.MustRevalidateAtUtc > DateTime.UtcNow -> Resp cacheResult
            | DoNotCache _
            | HardUtc _ -> Req request
            | Soft s ->
                addValidationHeaders request s.Validator
                ReqWithValidation (request, cacheResult, isStrongValidation s.Validator)

        let execute (cache: ICachingHttpClientDependencies) =
            match cacheBehavior with
            | Resp x -> 
                FromCache x
                |> asyncRetn
            | ReqWithValidation (req, cachedResp, strongValidation) ->
                cache.Send (req, token)
                |> asyncMap (fun resp -> (cachedResp, resp, strongValidation))
                |> asyncMap Hybrid
            | Req x ->
                cache.Send (x, token)
                |> asyncMap FromServer
        
        execute
        >> asyncMap Some
        |> Reader.Reader

    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Caching
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Caching#Varying_responses
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Caching#Targets_of_caching_operations
    // https://www.keycdn.com/blog/http-cache-headers
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Last-Modified
    // https://tools.ietf.org/id/draft-ietf-httpbis-cache-01.html#response.cacheability
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Expires
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Cache-Control
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-None-Match
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-Match
    // https://blog.bigbinary.com/2016/03/08/rails-5-switches-from-strong-etags-to-weak-tags.html
    // proxy-revalidate header ???
    // https://www.w3.org/Protocols/rfc2616/rfc2616-sec13.html

    let combineOptions o1 o2 =
        match o1, o2 with
        | Some x, Some y -> Some (x, y)
        | _ -> None

    let tryCacheValue  (response: HttpResponseMessage) =
        // TODO: http methods, response codes
        let req = buildCachedRequest response.RequestMessage

        let execute (cache: ICachingHttpClientDependencies) =

            let buildCacheKey isPrivate =
                match isPrivate with
                | true ->
                    cache.Cache.BuildUserKey
                    >> Option.map (buildUserCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri)
                    |> asyncMap
                    <| req
                | false ->
                    buildSharedCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri
                    |> Some
                    |> asyncRetn

            let shouldCache (model: CachedValues) =
                match model.CacheSettings.ExpirySettings with
                | DoNotCache -> None
                | HardUtc x when x > DateTime.UtcNow -> None
                | _ -> Some () 

            let addToCache (model: CachedValues) =
                let cache key =
                    let cachePut (k, ()) = async {
                        use! strm = 
                            Serializer.serialize model
                            |> asyncMap (fun (_, strm) -> strm)

                        return! cache.Cache.Put (k, (), Disposables.getValue strm)
                    }

                    shouldCache model
                    |> combineOptions key
                    |> Option.map cachePut
                    |> Option.defaultValue asyncUnit

                buildCacheKey model.CacheSettings.SharedCache
                |> asyncBind cache

            build response |> addToCache

        Reader.Reader execute
        |> ReaderAsync.map (fun () -> response)
       
    let toNull = function
        | Some x -> x
        | None -> null

    let combine (cacheResponse: CachedResponse) (serviceResponse: HttpResponseMessage) isStrongValidation =
        match isStrongValidation with
        | true -> toHttpResponseMessage cacheResponse
        | false ->
            let cachedContent = 
                cacheResponse.Content
                |> Option.map toHttpContent
                |> toNull

            // TODO: if serviceResponse has no validation headers,
            // should we append the headers from the previous req?
            serviceResponse.Content <- cachedContent
            serviceResponse

    let combineCacheResult (cacheResponse, serviceResponse: HttpResponseMessage, isStrongValidation) =
        match serviceResponse.StatusCode with
        | HttpStatusCode.NotModified -> combine cacheResponse serviceResponse isStrongValidation
        | _ -> serviceResponse 
        |> FromServer
        |> ReaderAsync.retn

    let rec cacheValue = function
        | Hybrid x -> combineCacheResult x |> ReaderAsync.bind cacheValue
        | FromCache x -> x.HttpResponse |> ReaderAsync.retn
        | FromServer x -> tryCacheValue x

open Private

let client (httpRequest: HttpRequestMessage, cancellationToken: CancellationToken) =
    
    let validateCachedResult = sendHttpRequest (httpRequest, cancellationToken)
    let sendFromHttpClient () = 
        fun (cache: ICachingHttpClientDependencies) ->
            cache.Send (httpRequest, cancellationToken)
        |> Reader.Reader
        |> ReaderAsync.map FromServer

    buildCachedRequest httpRequest
    |> asyncMap Some
    |> Reader.retn
    |> ReaderAsyncOption.bind tryGetCacheResult
    |> ReaderAsyncOption.bind validateCachedResult
    |> ReaderAsyncOption.defaultWith sendFromHttpClient
    |> ReaderAsync.bind cacheValue