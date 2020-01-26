module ShinyHttp.CachingHttpClient

open System
open System.Net.Http.Headers
open System.Net.Http
open System.Threading
open ShinyHttp.Headers.Parser
open ShinyHttp.Headers.CacheTime
open ReaderMonad

type ICache =
    abstract member Get : string -> (HttpResponseMessage * DateTime) option Async
    abstract member Put : (string * HttpResponseMessage * DateTime) -> unit Async
    abstract member Invalidate : string -> unit Async
    abstract member BuildUserKey : HttpRequestMessage -> string option

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
        
    let buildUserCacheKey (uri: Uri) (userKey: string) =
        let userKey = if String.IsNullOrEmpty userKey then "" else userKey

        sprintf "$:%s$:%O"
        <| userKey.Replace("$", "$$")
        <| uri

    let buildSharedCacheKey (uri: Uri) = buildUserCacheKey uri ""

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
                |> Option.map (buildUserCacheKey req.RequestUri)
                |> Option.map cache.Cache.Get
                |> traverseAsyncOpt
                |> asyncMap squashOptions

            let sharedResult = 
                buildSharedCacheKey req.RequestUri
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

    type CacheResult =
        {
            response: HttpResponseMessage
            //requiresReValidation: bool
        }

    let buildCacheResult (response: HttpResponseMessage, cacheUntil: DateTime) =
        // let requiresReValidation =
        //     if cacheUntil < DateTime.UtcNow then
        //         true
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
        | FromCache of HttpResponseMessage
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
    // https://www.keycdn.com/blog/http-cache-headers
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Last-Modified
    // https://tools.ietf.org/id/draft-ietf-httpbis-cache-01.html#response.cacheability
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Expires
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Cache-Control
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-None-Match
    // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-Match
    // https://blog.bigbinary.com/2016/03/08/rails-5-switches-from-strong-etags-to-weak-tags.html

    let combineOptions o1 o2 =
        match o1, o2 with
        | Some x, Some y -> Some (x, y)
        | _ -> None

    let tryCacheValue  (response: HttpResponseMessage) =
        // TODO: http methods, response codes

        let execute (cache: ICachingHttpClientDependencies) =

            let buildCacheKey (x: CacheControlHeaderValue option) =
                match x with
                | Some x when x.Private ->
                    cache.Cache.BuildUserKey response.RequestMessage
                    |> Option.map (buildUserCacheKey response.RequestMessage.RequestUri)
                | _ ->
                    buildSharedCacheKey response.RequestMessage.RequestUri
                    |> Some

            let addToCache (headers: HttpServerCacheHeaders) =
                let now = DateTime.UtcNow
                let cacheUntil = 
                    getCacheTime headers
                    |> Option.bind (function
                        | x when x <= TimeSpan.Zero -> None
                        | x when DateTime.MaxValue - x < now -> Some DateTime.MaxValue
                        | x -> now + x |> Some)

                let key = buildCacheKey headers.CacheControl

                combineOptions key cacheUntil
                |> Option.map (fun (k, t) -> cache.Cache.Put (k, response, t))
                |> Option.defaultValue asyncUnit

            parse response |> addToCache

        Reader.Reader execute
        |> ReaderAsync.map (fun () -> response)

    let cacheValue = function
        | FromCache x -> ReaderAsync.retn x
        | FromServer x -> tryCacheValue x
     //   | ValidatedFromServer x -> tryCacheValue x

open Private

let client (httpRequest: HttpRequestMessage, cancellationToken: CancellationToken) =
    
    let sendFromInsideCache = sendHttpRequest (httpRequest, cancellationToken)
    let sendFromHttpClient () = 
        fun (cache: ICachingHttpClientDependencies) ->
            cache.Send (httpRequest, cancellationToken)
        |> Reader.Reader
        |> ReaderAsync.map FromServer

    tryGetCacheResult httpRequest
    |> ReaderAsyncOption.map buildCacheResult
    |> ReaderAsyncOption.bind sendFromInsideCache
    |> ReaderAsyncOption.defaultWith sendFromHttpClient
    |> ReaderAsync.bind cacheValue