module ShinyHttpCache.FSharp.CachingHttpClient

open System
open System.Net.Http.Headers
open System.Net.Http
open System.Threading
open ShinyHttpCace.Utils
open ShinyHttpCache
open ShinyHttpCache.Dependencies
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils.ReaderMonad
open ShinyHttpCache.Model
open System.Net
open ShinyHttpCache.Serialization;
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
        | x when x = HttpMethod.Get -> Some "G"
        // TODO, more methods (especially put with etag)
        | _ -> None
        
    let buildUserCacheKey method (uri: Uri) (userKey: string) =
        // TODO: include http method
        // TODO: allow custom key build method (e.g. to include headers)
        let userKey = if String.IsNullOrEmpty userKey then "" else userKey
        
        getMethodKey method
        |> Option.map (fun m ->
            sprintf "%s$:%s$:%O"
            <| m
            <| userKey.Replace("$", "$$")
            <| uri)

    let buildSharedCacheKey method (uri: Uri) = buildUserCacheKey method uri ""
    
    let tryGetCacheResult req =
        let execute (cache: CachingHttpClientDependencies) =
            let userKey = buildUserKey cache req

            let reqMethod = HttpMethod(req.Method)
            let userResult = 
                userKey 
                |> Option.bind (buildUserCacheKey reqMethod req.Uri)
                |> Option.map (get cache)
                |> Infra.AsyncOpt.traverse
                |> Infra.Async.map Infra.Option.squash

            let sharedResult = 
                buildSharedCacheKey reqMethod req.Uri
                |> Option.map (get cache)
                |> Infra.AsyncOpt.traverse
                |> Infra.Async.map Infra.Option.squash

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
            | None -> Resp cacheResult
            | Some s when s.MustRevalidateAtUtc > DateTime.UtcNow -> Resp cacheResult
            | Some s ->
                addValidationHeaders request s.Validator
                ReqWithValidation (request, cacheResult, getValidationReason s.Validator)

        let execute (cache: CachingHttpClientDependencies) =
            match cacheBehavior with
            | Resp x -> 
                FromCache x
                |> Infra.Async.retn
            | ReqWithValidation (req, cachedResp, strongValidation) ->
                send cache req token
                |> Infra.Async.map (fun resp -> (cachedResp, resp, strongValidation))
                |> Infra.Async.map Hybrid
            | Req x ->
                send cache x token
                |> Infra.Async.map FromServer
        
        execute
        >> Infra.Async.map Some
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
                    >> Option.bind (buildUserCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri)
                    |> Infra.Async.map
                    <| req
                | true ->
                    buildSharedCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri
                    |> Infra.Async.retn

            let shouldCache (model: CachedValues) =
                match model.CacheSettings.ExpirySettings with
                | _ -> Some ()
                // TODO: this function used to be:
                // Either
                // 1. remove it
                // 2. add more checks (e.g. where are the checks for no-cache, no-store etc?)
//                match model.CacheSettings.ExpirySettings with
//                | HardUtc x when x > DateTime.UtcNow -> None
//                | _ -> Some () 

            let addToCache (model: CachedValues) =
                
                let cache key =
                    let cachePut (k, ()) = async {
                        use! strm = 
                            Serializer.serialize model
                            |> Infra.Async.map (fun (_, strm) -> strm)

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
                    |> Option.defaultValue Infra.Async.unit

                buildCacheKey model.CacheSettings.SharedCache
                |> Infra.Async.bind cache

            build response 
            |> Option.map addToCache
            |> Infra.AsyncOpt.traverse

        Reader.Reader execute
        |> ReaderAsyncOption.map (fun () -> response)
       
    let toNull = function
        | Some x -> x
        | None -> null
       
    let toOption = function
        | x when isNull x -> None
        | x -> Some x

    let combineCacheResult (cacheResponse, serviceResponse: HttpResponseMessage, isStrongValidation) =
            
        let copyCacheHeaders (from: HttpContentHeaders) (``to``: HttpContentHeaders) =
            ``to``.Expires <- from.Expires
            ``to``.LastModified <- from.LastModified
        
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
    |> Infra.Async.map Some
    |> Reader.retn
    |> ReaderAsyncOption.bind tryGetCacheResult
    |> ReaderAsyncOption.bind validateCachedResult
    |> ReaderAsyncOption.defaultWith sendFromHttpClient
    |> ReaderAsync.bind cacheValue
    |> Reader.mapReader Dependencies.create