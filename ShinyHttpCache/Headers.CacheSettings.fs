module ShinyHttpCache.Headers.CacheSettings

open System
open System.Net.Http.Headers
open System.Text.RegularExpressions


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

type Validator =
    | ETag of EntityTag
    | ExpirationDateUtc of DateTime
    | Both of (EntityTag * DateTime)

type RevalidationSettings = 
    {
        MustRevalidateAtUtc: DateTime
        Validator: Validator
    }

type ExpirySettings =
    | NoExpiryDate
    | Soft of RevalidationSettings
    | HardUtc of DateTime

module Private =

    let nullableToOption (x: Nullable<'a>) = match x.HasValue with | true -> Some x.Value | false -> None

    module Immutable =
        let private hasImmutable = Regex("immutable", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
        
        let get (cacheControl: CacheControlHeaderValue) =
            match cacheControl.ToString() with
            | x when hasImmutable.IsMatch x -> Some NoExpiryDate 
            | _ -> None

    module ETagValues =
        
        let get = 
            fun (headers: Parser.HttpServerCacheHeaders) -> headers.ETag
            >> Option.map (function | x when x.IsWeak -> Weak x.Tag | x -> Strong x.Tag)

    module Expires =
        
        let get (headers: Parser.HttpServerCacheHeaders) = headers.ExipiresUtc

    module MaxAge =
        
        let get = 
            fun (headers: Parser.HttpServerCacheHeaders) -> headers.CacheControl
            >> Option.map (fun x -> x.MaxAge)
            >> Option.bind nullableToOption

    module SMaxAge =
        
        let get = 
            fun (headers: Parser.HttpServerCacheHeaders) -> headers.CacheControl
            >> Option.map (fun x -> x.SharedMaxAge)
            >> Option.bind nullableToOption

    module ExpirySettings =

        let private isSharedCache = function
            | Some (headers: CacheControlHeaderValue) -> not headers.Private
            | None -> true

        let get requestTime (headers: Parser.HttpServerCacheHeaders) =
            let eTag = ETagValues.get headers
            let expires = Expires.get headers
            let age =
                match isSharedCache headers.CacheControl with
                | false -> MaxAge.get headers
                | true -> 
                    match SMaxAge.get headers with
                    | Some x -> Some x
                    | None -> MaxAge.get headers

            match age, expires, eTag with
            | None, None, None -> None
            | Some x, None, None -> requestTime + x |> HardUtc |> Some
            | None, Some x, None ->
                {
                    MustRevalidateAtUtc = x
                    Validator = ExpirationDateUtc x
                }
                |> Soft
                |> Some
            | None, None, Some x ->
                {
                    MustRevalidateAtUtc = requestTime
                    Validator = ETag x
                }
                |> Soft
                |> Some
            | None, Some x, Some y ->
                {
                    MustRevalidateAtUtc = requestTime
                    Validator = Both (y, x)
                }
                |> Soft
                |> Some
            | Some x, Some y, None ->
                {
                    MustRevalidateAtUtc = requestTime + x
                    Validator = ExpirationDateUtc y
                }
                |> Soft
                |> Some
            | Some x, None, Some y ->
                {
                    MustRevalidateAtUtc = requestTime + x
                    Validator = ETag y
                }
                |> Soft
                |> Some
            | Some x, Some y, Some z ->
                {
                    MustRevalidateAtUtc = requestTime + x
                    Validator = Both (z, y)
                }
                |> Soft
                |> Some

    let pickFirstValue =
        Seq.ofArray
        >> Seq.map (fun f -> f())
        >> Seq.filter Option.isSome
        >> Seq.map Option.get
        >> Seq.tryHead

    module NoStore =
        let get requestTime (headers: CacheControlHeaderValue) = 
            match headers.NoStore with
            | true -> HardUtc requestTime |> Some
            | false -> None

open Private

let build (headers: Parser.HttpServerCacheHeaders) =

    // TODO: find actual request time
    let requestTime = DateTime.UtcNow

    [|
        fun () -> headers.CacheControl |> Option.bind (NoStore.get requestTime)
        fun () -> headers |> ExpirySettings.get requestTime

        // TODO: move this to 1st priority when https://tools.ietf.org/html/rfc8246 becomes "Proposed Standard"
        fun () -> headers.CacheControl |> Option.bind Immutable.get
    |]
    |> pickFirstValue