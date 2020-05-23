module ShinyHttpCache.FSharp.CachingHttpClient

open System
open System.Net.Http.Headers
open System.Net.Http
open System.Threading
open ShinyHttpCache
open ShinyHttpCache.Dependencies
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils.ReaderMonad
open ShinyHttpCache.Model
open ShinyHttpCache.Model.CacheSettings
open System.Net
open System.IO
open ShinyHttpCache.Serialization;
open ShinyHttpCache.Model
open ShinyHttpCache.Utils

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
        let execute (cache: CachingHttpClientDependencies) =
            let userKey = buildUserKey cache req

            let reqMethod = HttpMethod(req.Method)
            let userResult = 
                userKey 
                |> Option.map (buildUserCacheKey reqMethod req.Uri)
                |> Option.map (get cache)
                |> traverseAsyncOpt
                |> asyncMap squashOptions

            let sharedResult = 
                buildSharedCacheKey reqMethod req.Uri
                |> get cache

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
        
    type HeadersAreValidated = | Yes | No

    type HttpResponseType =
        | FromCache of CachedValues
        | FromServer of HttpResponseMessage
        // TODO: reduce the scope of the first arg to just the response
        // last arg is "StrongValidation"
        | Hybrid of (CachedValues * HttpResponseMessage * HeadersAreValidated)

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
        | ReqWithValidation of (HttpRequestMessage * CachedValues * HeadersAreValidated)
        | Resp of CachedValues

    let getValidationReason = function
        | Both (x, _)
        | ETag x -> match x with | Strong _ -> HeadersAreValidated.Yes | _ -> HeadersAreValidated.No
        | ExpirationDateUtc _ -> HeadersAreValidated.No

    let sendHttpRequest (request, token) (cacheResult: CachedValues) =
        let cacheBehavior =
            match cacheResult.CacheSettings.ExpirySettings with
            | NoExpiryDate -> Resp cacheResult
            | HardUtc exp when exp > DateTime.UtcNow -> Resp cacheResult
            | Soft s when s.MustRevalidateAtUtc > DateTime.UtcNow -> Resp cacheResult
            | HardUtc _ -> Req request
            | Soft s ->
                addValidationHeaders request s.Validator
                ReqWithValidation (request, cacheResult, getValidationReason s.Validator)

        let execute (cache: CachingHttpClientDependencies) =
            match cacheBehavior with
            | Resp x -> 
                FromCache x
                |> asyncRetn
            | ReqWithValidation (req, cachedResp, strongValidation) ->
                send cache req token
                |> asyncMap (fun resp -> (cachedResp, resp, strongValidation))
                |> asyncMap Hybrid
            | Req x ->
                send cache x token
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

        let execute (cache: CachingHttpClientDependencies) =

            let buildCacheKey isPublic =
                match isPublic with
                | false ->
                    buildUserKey cache
                    >> Option.map (buildUserCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri)
                    |> asyncMap
                    <| req
                | true ->
                    buildSharedCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri
                    |> Some
                    |> asyncRetn

            let shouldCache (model: CachedValues) =
                match model.CacheSettings.ExpirySettings with
                | HardUtc x when x > DateTime.UtcNow -> None
                | _ -> Some () 

            let addToCache (model: CachedValues) =
                
                let cache key =
                    let cachePut (k, ()) = async {
                        use! strm = 
                            Serializer.serialize model
                            |> asyncMap (fun (_, strm) -> strm)

                        let metadata =
                            {
                                CacheSettings = model.CacheSettings
                                GetRawContent = model.HttpResponse.Content.ReadAsByteArrayAsync >> Async.AwaitTask
                            } : Dependencies.CacheMetadata

                        return! put cache k (Disposables.getValue strm) metadata
                    }

                    shouldCache model
                    |> combineOptions key
                    |> Option.map cachePut
                    |> Option.defaultValue asyncUnit

                buildCacheKey model.CacheSettings.SharedCache
                |> asyncBind cache

            build response 
            |> Option.map addToCache
            |> traverseAsyncOpt

        Reader.Reader execute
        |> ReaderAsyncOption.map (fun () -> response)
       
    let toNull = function
        | Some x -> x
        | None -> null
       
    let toOption = function
        | x when isNull x -> None
        | x -> Some x
        
    let copyCacheHeaders (from: HttpContentHeaders) (``to``: HttpContentHeaders) =
        ``to``.Expires <- from.Expires
        ``to``.LastModified <- from.LastModified

    let combineCacheResult (cacheResponse, serviceResponse: HttpResponseMessage, isStrongValidation) =
        
        match serviceResponse.StatusCode with
        | HttpStatusCode.NotModified ->
            match isStrongValidation with
            | HeadersAreValidated.Yes -> FromCache cacheResponse
            | HeadersAreValidated.No ->
                // TODO: need to really think about the consequences of this (partial headers)
                copyCacheHeaders serviceResponse.Content.Headers cacheResponse.HttpResponse.Content.Headers
                
                serviceResponse.Content <- cacheResponse.HttpResponse.Content
                FromServer serviceResponse
        | _ -> serviceResponse |> FromServer
        |> ReaderAsync.retn

    let rec cacheValue = function
        | Hybrid x -> combineCacheResult x |> ReaderAsync.bind cacheValue
        | FromCache x -> x.HttpResponse |> ReaderAsync.retn
        | FromServer x ->
            tryCacheValue x
            |> ReaderAsync.map (Option.defaultValue x)

open Private

let client (httpRequest: HttpRequestMessage, cancellationToken: CancellationToken) =
    
    let validateCachedResult = sendHttpRequest (httpRequest, cancellationToken)
    let sendFromHttpClient () = 
        fun (cache: CachingHttpClientDependencies) ->
            send cache httpRequest cancellationToken
        |> Reader.Reader
        |> ReaderAsync.map FromServer

    buildCachedRequest httpRequest
    |> asyncMap Some
    |> Reader.retn
    |> ReaderAsyncOption.bind tryGetCacheResult
    |> ReaderAsyncOption.bind validateCachedResult
    |> ReaderAsyncOption.defaultWith sendFromHttpClient
    |> ReaderAsync.bind cacheValue
    |> Reader.mapReader Dependencies.create