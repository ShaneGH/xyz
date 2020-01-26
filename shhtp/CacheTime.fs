module ShinyHttp.Headers.CacheTime

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

module private Private =

    let pickFirstValue =
        Seq.ofArray
        >> Seq.map (fun f -> f())
        >> Seq.filter Option.isSome
        >> Seq.map Option.get
        >> Seq.tryHead

    let nullableToOption (x: Nullable<'a>) = match x.HasValue with | true -> Some x.Value | false -> None

    let getSharedMaxAge shared (cacheControl: CacheControlHeaderValue) =
        cacheControl.SharedMaxAge
        |> nullableToOption
        |> Option.bind (fun x ->
            if shared then Some x
            else None)

    let hasImmutable = Regex("immutable", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
    let isImmutable (cacheControl: CacheControlHeaderValue) =
        // TODO: this is a guess
        match cacheControl.ToString() with
        | x when hasImmutable.IsMatch x -> true
        | _ -> false

    let cacheControlFilter sharedCache (cacheControl: CacheControlHeaderValue) =

        let noStore () = match cacheControl.NoStore with | true -> Some TimeSpan.Zero | false -> None
        let sMaxAge () = getSharedMaxAge sharedCache cacheControl
        let maxAge () = nullableToOption cacheControl.MaxAge
        let immutable () = match isImmutable cacheControl with true -> Some TimeSpan.MaxValue | false -> None
               
        [|
            noStore
            sMaxAge
            maxAge
            immutable
        |]
        |> pickFirstValue

    let max x y =
        match y > x with
        | true -> y
        | false -> x

open Private

let getCacheTime (headers: Parser.HttpServerCacheHeaders) =

    let cacheControl () = headers.CacheControl |> Option.bind (cacheControlFilter headers.SharedCache)

    let expires () = headers.ExipiresUtc |> Option.map (fun x -> x - DateTime.UtcNow)

    [|
        cacheControl
        expires
    |]
    |> pickFirstValue
    |> Option.map (max TimeSpan.Zero)