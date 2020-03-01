module ShinyHttpCache.Headers.Parser
open System
open System.Net.Http.Headers
open System.Net.Http

type HttpServerCacheHeaders =
    {
        CacheControl: CacheControlHeaderValue option
        Pragma: string option
        ETag: EntityTagHeaderValue option
        ExipiresUtc: DateTime option
        LasModifiedUtc: DateTime option
        Vary: string option
    }

module private Private =

    let nullToNone = function | null -> None | y -> Some y

    let nullableToNone (x: 'a Nullable) = match x.HasValue with | true -> Some x.Value | false -> None
        
    let parseHeaders (headers: HttpResponseHeaders) expires lastModified =

        {
            CacheControl = nullToNone headers.CacheControl
            Pragma = None // TODO
            ExipiresUtc = expires
            ETag = nullToNone headers.ETag
            LasModifiedUtc = lastModified
            Vary = None // TODO
        }

open Private

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