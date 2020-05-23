module ShinyHttpCache.Model
open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text.RegularExpressions

module CacheSettings =

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

    type EntityTag =
        | Strong of string
        | Weak of string

    let private buildETag (etag: EntityTagHeaderValue) = 
        match etag.IsWeak with
        | true -> Weak etag.Tag
        | false -> Strong etag.Tag

    type Validator =
        | ETag of EntityTag
        | ExpirationDateUtc of DateTime
        | Both of (EntityTag * DateTime)

    type RevalidationSettings = 
        {
            MustRevalidateAtUtc: DateTime
            Validator: Validator
        }

    type CacheSettings =
        {
            ExpirySettings: RevalidationSettings option
            SharedCache: bool
        }
       
    let private hasImmutable = Regex("immutable", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)

    let build (response: HttpResponseMessage) = 

        let sharedCache =
            match response.Headers.CacheControl with
            | null -> true
            | x -> not x.Private

        let immutable =
            match response.Headers.CacheControl with
            | null -> false
            | x when hasImmutable.IsMatch (x.ToString()) -> true
            | _ -> false

        let noStore =
            match response.Headers.CacheControl with
            | null -> false
            | x when x.NoStore -> true
            | _ -> false
                
        let etag =
            match response.Headers.ETag with
            | null -> None
            | x -> buildETag x |> Some

        let maxAge =
            match sharedCache, response.Headers.CacheControl with
            | _, null -> None
            | false, x when not x.MaxAge.HasValue -> None
            | false, x -> Some x.MaxAge.Value
            | true, x when not x.SharedMaxAge.HasValue -> 
                match x.MaxAge.HasValue with
                | true -> Some x.MaxAge.Value
                | false -> None
            | true, x -> Some x.SharedMaxAge.Value

        let expires =
            match response.Content with
            | null -> None
            | x when not x.Headers.Expires.HasValue -> None
            | x -> Some x.Headers.Expires.Value

        let expires =
            match maxAge, expires with
            | None, None -> None
            | Some x, _ -> DateTime.UtcNow + x |> Some
            | None, Some x -> x.UtcDateTime |> Some

        match noStore, immutable, etag, expires with
        | true, _, _, _
        | false, false, None, None -> None
        | false, false, Some x, None -> 
            {
                SharedCache = sharedCache
                ExpirySettings = 
                    {
                        MustRevalidateAtUtc = DateTime.UtcNow
                        Validator = ETag x
                    } |> Some 
            } |> Some
        | false, false, None, Some x-> 
            {
                SharedCache = sharedCache
                ExpirySettings = 
                    {
                        MustRevalidateAtUtc = x
                        Validator = ExpirationDateUtc x
                    } |> Some
            } |> Some
        | false, false, Some x, Some y-> 
            {
                SharedCache = sharedCache
                ExpirySettings = 
                    {
                        MustRevalidateAtUtc = y
                        Validator = Both (x, y)
                    } |> Some
            } |> Some
        // TODO: move this to higher priority when https://tools.ietf.org/html/rfc8246 becomes "Proposed Standard"
        | false, true, _, _ ->
            {
                SharedCache = sharedCache
                ExpirySettings = None
            } |> Some

type CachedValues =
    {
        HttpResponse: HttpResponseMessage
        CacheSettings: CacheSettings.CacheSettings
    }

let build response =
    CacheSettings.build response
    |> Option.map (fun x ->
        {
            HttpResponse = response
            CacheSettings = x
        })

    


// module Private =

//     let nullableToOption (x: Nullable<'a>) = match x.HasValue with | true -> Some x.Value | false -> None

//     module Immutable =
//         let private hasImmutable = Regex("immutable", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
        
//         let get (cacheControl: CacheControlHeaderValue) =
//             match cacheControl.ToString() with
//             | x when hasImmutable.IsMatch x -> Some NoExpiryDate 
//             | _ -> None

//     module ETagValues =
        
//         let get = 
//             fun (headers: Parser.HttpServerCacheHeaders) -> headers.ETag
//             >> Option.map (function | x when x.IsWeak -> CacheSettings.Weak x.Tag | x -> CacheSettings.Strong x.Tag)

//     module Expires =
        
//         let get (headers: Parser.HttpServerCacheHeaders) = headers.ExipiresUtc

//     module MaxAge =
        
//         let get = 
//             fun (headers: Parser.HttpServerCacheHeaders) -> headers.CacheControl
//             >> Option.map (fun x -> x.MaxAge)
//             >> Option.bind nullableToOption

//     module SMaxAge =
        
//         let get = 
//             fun (headers: Parser.HttpServerCacheHeaders) -> headers.CacheControl
//             >> Option.map (fun x -> x.SharedMaxAge)
//             >> Option.bind nullableToOption

//     module SharedCache =

//         let get = function
//             | Some (headers: CacheControlHeaderValue) -> not headers.Private
//             | None -> true

//     module ExpirySettings =

//         let get requestTime (headers: Parser.HttpServerCacheHeaders) =
//             let publicCache = SharedCache.get headers.CacheControl
//             let eTag = ETagValues.get headers
//             let expires = Expires.get headers
//             let age =
//                 match publicCache with
//                 | false -> MaxAge.get headers
//                 | true -> 
//                     match SMaxAge.get headers with
//                     | Some x -> Some x
//                     | None -> MaxAge.get headers

//             match age, expires, eTag with
//             | None, None, None -> None
//             | Some x, None, None -> requestTime + x |> HardUtc |> Some
//             | None, Some x, None ->
//                 {
//                     MustRevalidateAtUtc = x
//                     Validator = ExpirationDateUtc x
//                 }
//                 |> Soft
//                 |> Some
//             | None, None, Some x ->
//                 {
//                     MustRevalidateAtUtc = requestTime
//                     Validator = ETag x
//                 }
//                 |> Soft
//                 |> Some
//             | None, Some x, Some y ->
//                 {
//                     MustRevalidateAtUtc = requestTime
//                     Validator = Both (y, x)
//                 }
//                 |> Soft
//                 |> Some
//             | Some x, Some y, None ->
//                 {
//                     MustRevalidateAtUtc = requestTime + x
//                     Validator = ExpirationDateUtc y
//                 }
//                 |> Soft
//                 |> Some
//             | Some x, None, Some y ->
//                 {
//                     MustRevalidateAtUtc = requestTime + x
//                     Validator = ETag y
//                 }
//                 |> Soft
//                 |> Some
//             | Some x, Some y, Some z ->
//                 {
//                     MustRevalidateAtUtc = requestTime + x
//                     Validator = Both (z, y)
//                 }
//                 |> Soft
//                 |> Some
//             |> Option.map (fun x -> { ExpirySettings = x; SharedCache = publicCache })

//     let pickFirstValue =
//         Seq.ofArray
//         >> Seq.map (fun f -> f())
//         >> Seq.filter Option.isSome
//         >> Seq.map Option.get
//         >> Seq.tryHead

//     module NoStore =
//         let get requestTime (headers: CacheControlHeaderValue) = 
//             match headers.NoStore with
//             | true -> HardUtc requestTime |> Some
//             | false -> None

// open Private

// type CachedValues =
//     {
//         HttpResponse: CachedResponse
//         CacheSettings: CacheSettings
//     }

// let build (headers: Parser.HttpServerCacheHeaders) =

//     // TODO: find actual request time
//     let requestTime = DateTime.UtcNow

//     let convertToCacheSettings expirySettings =
//         {
//             ExpirySettings = expirySettings
//             SharedCache = SharedCache.get headers.CacheControl
//         }

//     [|
//         // check no store
//         fun () -> 
//             headers.CacheControl 
//             |> Option.bind (NoStore.get requestTime)
//             |> Option.map convertToCacheSettings

//         // check other headers
//         fun () -> headers |> ExpirySettings.get requestTime

//         // TODO: move this to 1st priority when https://tools.ietf.org/html/rfc8246 becomes "Proposed Standard"
//         // check immutable header
//         fun () -> 
//             headers.CacheControl 
//             |> Option.bind Immutable.get
//             |> Option.map convertToCacheSettings
//     |]
//     |> pickFirstValue