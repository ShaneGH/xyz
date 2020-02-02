﻿module ShinyHttpCache.CachingHttpClient

open System
open System.Net.Http.Headers
open System.Net.Http
open System.Threading
open ShinyHttpCache.Headers.Parser
open ShinyHttpCache.Headers.CacheTime
open ShinyHttpCache.Headers.CacheSettings
open ReaderMonad
open System.Collections.Generic


type CacheSettings =
    {
        ExpirySettings: ExpirySettings
        PublicCache: bool
    }

type CachedValues =
    {
        HttpResponse: CachedResponse.CachedResponse
        CacheSettings: CacheSettings
    }

type ICache =
    abstract member Get : string -> CachedValues option Async
    abstract member Put : (string * CachedValues) -> unit Async
    abstract member Delete : string -> unit Async
    abstract member BuildUserKey : CachedRequest.CachedRequest -> string option

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

            let userResult = 
                userKey 
                |> Option.map (buildUserCacheKey req.Method req.Uri)
                |> Option.map cache.Cache.Get
                |> traverseAsyncOpt
                |> asyncMap squashOptions

            let sharedResult = 
                buildSharedCacheKey req.Method req.Uri
                |> cache.Cache.Get

            async {
                let! usr = userResult
                let! shr = sharedResult

                match usr, shr with
                | Some x, _
                | _, Some x -> return Some x
                | _ -> return None
            }

        Reader.Reader execute

    let isCacheControl (x: KeyValuePair<string, string list>) = "Cache-Control".Equals(x.Key, StringComparison.InvariantCultureIgnoreCase)

    let getCacheControl =
        Seq.ofList
        >> Seq.filter isCacheControl
        >> Seq.fold (fun s x -> List.concat [s; x.Value] ) []
        >> Seq.map CacheControlHeaderValue.TryParse
        >> Seq.filter (fun (x, _) -> x)
        >> Seq.map (fun (_, x) -> x)
        >> Seq.tryHead

    type CacheResult =
        {
            response: CachedResponse.CachedResponse
            //requiresReValidation: bool
        }

    let buildCacheResult (response: CachedResponse.CachedResponse) =
        // let requiresReValidation =
              
        //     let cacheControl = lazy getCacheControl response.Headers
        //     let conditions =
        //         [|
        //             fun () -> response.ExpirationDateUtc >= DateTime.UtcNow
        //             fun () ->
        //                 let cacheControl = 
        //                 cacheControl.Value.MustRevalidate
        //                 response.Headers
        //                 |> Option.bind (fun x -> x)
        //         |]

        //     if response.ExpirationDateUtc < DateTime.UtcNow then
        //         false
        //     else if isNull response.Headers || isNull response.Headers.CacheControl then
        //         false
        //     else if response.Headers.CacheControl.Private then
        //         response.Headers.CacheControl.MustRevalidate
        //     else
        //         response.Headers.CacheControl.MustRevalidate || response.Headers.CacheControl.ProxyRevalidate
           

        {
            response = response
            //requiresReValidation = requiresReValidation
        }

    type HttpResponseType =
        | FromCache of CachedResponse.CachedResponse
        | FromServer of HttpResponseMessage
    //    | ValidatedFromServer of HttpResponseMessage

    // let addValidationHeaders (request: HttpRequestMessage) (cachedResponse: HttpResponseMessage) = 
    //     if isNull cachedResponse.Headers then request
    //     else
    //         request.Headers.IfNoneMatch.Add cachedResponse.Headers.ETag
    //         if isNull cachedResponse.Content then ()
    //         else request.Headers.IfModifiedSince <- cachedResponse.Content.Headers.LastModified
    //         request

    let sendHttpRequest (request, token) cacheResult =
        let execute (cache: ICachingHttpClientDependencies) =
            let addCachedContent result = result
            
            // if cacheResult.requiresReValidation then
            //     let request = addValidationHeaders request cacheResult.response
            //     cache.Send (request, token)
            //     |> asyncMap ValidatedFromServer //TODO if 304 add content from cache, if 200 add content to cache
            // else
            cacheResult.response |> FromCache |> asyncRetn
        
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

    let combineOptions o1 o2 =
        match o1, o2 with
        | Some x, Some y -> Some (x, y)
        | _ -> None

    let tryCacheValue  (response: HttpResponseMessage) =
        // TODO: http methods, response codes
        let req = CachedRequest.build response.RequestMessage

        let execute (cache: ICachingHttpClientDependencies) =

            let buildCacheKey (x: CacheControlHeaderValue option) =
                match x with
                | Some x when x.Private ->
                    cache.Cache.BuildUserKey
                    >> Option.map (buildUserCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri)
                    |> asyncMap
                    <| req
                | _ ->
                    buildSharedCacheKey response.RequestMessage.Method response.RequestMessage.RequestUri
                    |> Some
                    |> asyncRetn

            let addToCache (headers: HttpServerCacheHeaders): Unit Async =
                NotImplementedException("#######") |> raise
                // let now = DateTime.UtcNow
                // let expirySettings = 
                //     getCacheTime headers
                //     |> Option.bind (function
                //         | x when x <= TimeSpan.Zero -> None
                //         | x when DateTime.MaxValue - x < now -> Some DateTime.MaxValue
                //         | x -> now + x |> Some)

                // let cache key =
                //     let cachePut (k, t) =
                //         CachedResponse.build response
                //         |> asyncBind (fun resp -> cache.Cache.Put (k, resp))

                //     combineOptions key cacheUntil
                //     |> Option.map cachePut
                //     |> Option.defaultValue asyncUnit

                // buildCacheKey headers.CacheControl
                // |> asyncBind cache

            parse response |> addToCache

        Reader.Reader execute
        |> ReaderAsync.map (fun () -> response)

    let cacheValue = function
        | FromCache x -> x |> CachedResponse.toHttpResponseMessage true |> ReaderAsync.retn
        | FromServer x -> tryCacheValue x
     //   | ValidatedFromServer x -> tryCacheValue x

open Private

let client (httpRequest: HttpRequestMessage, cancellationToken: CancellationToken): Reader.Reader<ICachingHttpClientDependencies, HttpResponseMessage Async> =
                NotImplementedException("#######") |> raise
    
    // let sendFromInsideCache = sendHttpRequest (httpRequest, cancellationToken)
    // let sendFromHttpClient () = 
    //     fun (cache: ICachingHttpClientDependencies) ->
    //         cache.Send (httpRequest, cancellationToken)
    //     |> Reader.Reader
    //     |> ReaderAsync.map FromServer

    // CachedRequest.build httpRequest
    // |> asyncMap Some
    // |> Reader.retn
    // |> ReaderAsyncOption.bind tryGetCacheResult
    // |> ReaderAsyncOption.map buildCacheResult
    // |> ReaderAsyncOption.bind sendFromInsideCache
    // |> ReaderAsyncOption.defaultWith sendFromHttpClient
    // |> ReaderAsync.bind cacheValue