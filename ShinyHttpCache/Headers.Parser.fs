module ShinyHttpCache.Headers.Parser
open System
open System.Net.Http.Headers
open System.Globalization
open System.Net.Http

// type ClientCacheHeaders =
//     {
//         IfModifiedSince: string option
//         IfNoneMatch: string option
//     }

type ParseResult<'a> =
    | Success of 'a
    | Failure of string list
    | NoResult


module private Private1 =
    //let dateFormats = 
    // might need this in the future
    //    [|
    //        "ddd, dd MMM yyyy HH:mm:ss 'GMT'"
    //        "dddd, dd-MMM-yy HH:mm:ss 'GMT'"
    //        "ddd MMM d HH:mm:ss yyyy"
    //    |]
    //let parseDate d =
    //    let parseResult = DateTime.TryParseExact(d, dateFormats, CultureInfo.InvariantCulture,
    //                          DateTimeStyles.AllowInnerWhite)

    //    match parseResult with
    //    | (false, _) -> None
    //    | (true, x) -> DateTime(x.Ticks, DateTimeKind.Utc) |> Some

    let nullToNone = function | null -> None | y -> Some y

    let nullableToNone (x: 'a Nullable) = match x.HasValue with | true -> Some x.Value | false -> None
open Private1

type HttpServerCacheHeaders =
    {
        CacheControl: CacheControlHeaderValue option
        SharedCache: bool
        Pragma: string option
        ETag: EntityTagHeaderValue option
        ExipiresUtc: DateTime option
        LasModifiedUtc: DateTime option
        Vary: string option
    }

module private Private2 =
        
    let parseHeaders (headers: HttpResponseHeaders) expires lastModified =

        {
            SharedCache = true // TODO implement private cache
            CacheControl = nullToNone headers.CacheControl
            Pragma = None // TODO
            ExipiresUtc = expires
            ETag = nullToNone headers.ETag
            LasModifiedUtc = lastModified
            Vary = None // TODO
        }

open Private2

let parse (message: HttpResponseMessage) =
    let (expires, lastModified) = 
        if isNull message.Content || isNull message.Content.Headers
        then
            (None, None)
        else
            let exp =
                message.Content.Headers.Expires
                |> nullableToNone
                |> Option.map (fun x -> x.UtcDateTime)

            let lm =
                message.Content.Headers.LastModified
                |> nullableToNone
                |> Option.map (fun x -> x.UtcDateTime)

            (exp, lm)

    parseHeaders message.Headers expires lastModified