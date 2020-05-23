module ShinyHttpCache.Model
open System
open System.Net.Http
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

[<RequireQualifiedAccess>]
module CacheSettings =

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

    type ReValidationSettings = 
        {
            MustRevalidateAtUtc: DateTime
            Validator: Validator
        }
    
    type Value =
        {
            // if None, this does not expire
            ExpirySettings: ReValidationSettings option
            SharedCache: bool
        }

    let buildCacheSettings (response: HttpResponseMessage) = 

        let sharedCache =
            match response.Headers.CacheControl with
            | null -> true
            | x -> not x.Private

        let immutable =
            match response.Headers.CacheControl with
            | null -> false
            | xs when xs.Extensions <> null ->
                xs.Extensions
                |> Seq.filter (fun x -> x.Name = "immutable")
                |> Seq.map (fun _ -> true)
                |> Seq.tryHead
                |> Option.defaultValue false
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
        CacheSettings: CacheSettings.Value
    }

let build response =
    CacheSettings.buildCacheSettings response
    |> Option.map (fun x ->
        {
            HttpResponse = response
            CacheSettings = x
        })