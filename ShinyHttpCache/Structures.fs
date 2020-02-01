namespace ShinyHttpCache

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text

module private Private = 
    let asyncReturn x = async { return x }
    
    let asyncMap f x = async { 
        let! x1 = x
        return (f x1) 
    }

    let mapHeaderKvp (kvp: KeyValuePair<string, IEnumerable<'a>>) =
        new KeyValuePair<string, 'a list>(kvp.Key, List.ofSeq kvp.Value)

    let pointerSize = IntPtr.Size

    let measureHeaderSize (h: KeyValuePair<string, string list>) =
        // header
        pointerSize 
        // key
        + pointerSize + Encoding.UTF8.GetByteCount h.Key
        // values
        + pointerSize + List.fold (fun s (v: string) -> s + pointerSize + (Encoding.UTF8.GetByteCount v)) 0 h.Value

    let measureHeadersSize hs =
        // headers
        pointerSize 
        // each header
        + pointerSize + (List.sumBy measureHeaderSize hs)

    let toOption = function
        | x when isNull x -> None
        | x -> Some x

    let invertOpt = function
        | None -> asyncReturn None
        | Some x -> asyncMap Some x

open Private

module CachedContent =

    type CachedHttpContent (content: byte[], headers: KeyValuePair<string, string list> seq) as this =
        inherit HttpContent()
        do for header in headers do this.Headers.Add(header.Key, header.Value)
        
        override __.SerializeToStreamAsync (stream, _) =
            stream.WriteAsync (content, 0, content.Length)
        
        override __.TryComputeLength (length) =
            length <- content.LongLength
            true
    
    type CachedContent =
        {
            Headers: KeyValuePair<string, string list> list
            Content: byte array
        }
        
    let build (content: HttpContent) =
        async {
            let! c = content.ReadAsByteArrayAsync() |> Async.AwaitTask
            return {
                Headers = content.Headers
                   |> Seq.map mapHeaderKvp
                   |> List.ofSeq;
                Content = c
            }
        }
        
    let toHttpContent cachedContent =
        new CachedHttpContent (cachedContent.Content, cachedContent.Headers |> Seq.ofList)
        :> HttpContent
        
    let getRoughMemorySize cachedContent =
        let contentLength = cachedContent.Content.Length + pointerSize
        let headerLength = measureHeadersSize cachedContent.Headers

        pointerSize + contentLength + headerLength

module CachedRequest =
    
    type CachedRequest =
        {
            Version: Version;
            Method: HttpMethod;
            Uri: Uri;
            Content: CachedContent.CachedContent option;
            Headers: KeyValuePair<string, string list> list;
            // TODO: how do properties affect cache size
            Properties: KeyValuePair<string, obj> list;
        }
        
       // TODO: currently this method is called a few times
       // try to get this down to once
    let build (req: HttpRequestMessage) =
        req.Content 
        |> toOption
        |> Option.map CachedContent.build
        |> invertOpt
        |> asyncMap (fun c -> {
            Version = req.Version;
            Method = req.Method;
            Uri = req.RequestUri;
            Content = c;
            Headers = req.Headers
                |> Seq.map mapHeaderKvp
                |> List.ofSeq;
            Properties = req.Properties |> List.ofSeq;
        })
        
    let toHttpRequestMessage mapProperties req =
        let output = new HttpRequestMessage ()
        
        output.Version <- req.Version
        output.Method <- req.Method
        output.RequestUri <- req.Uri
        output.Content <- 
            req.Content 
            |> Option.map CachedContent.toHttpContent 
            |> Option.defaultValue null;
        
        for h in req.Headers do output.Headers.Add(h.Key, h.Value)
        
        if mapProperties then
            for p in req.Properties do output.Properties.Add(p.Key, p.Value)
        
        output

module CachedResponse =
    
    type CachedResponse =
        {
            Version: Version
            StatusCode: HttpStatusCode
            ReasonPhrase: string
            Content: CachedContent.CachedContent option
            Request: CachedRequest.CachedRequest
            Headers: KeyValuePair<string, string list> list
            ExpirationDateUtc: DateTime
            CanBeValidatedAfterExpiration: bool
        }
        
    let build expirationDateUtc canBeValidatedAfterExpiration (resp: HttpResponseMessage) =
        let c = 
            resp.Content 
            |> toOption 
            |> Option.map CachedContent.build 
            |> invertOpt
        let req = CachedRequest.build resp.RequestMessage
        async {
            let! c' = c
            let! r' = req
            
            return {
                Version = resp.Version;
                StatusCode = resp.StatusCode;
                ReasonPhrase = resp.ReasonPhrase;
                Content = c';
                Headers = resp.Headers 
                   |> Seq.map mapHeaderKvp
                   |> List.ofSeq;
                Request = r'
                ExpirationDateUtc = expirationDateUtc
                CanBeValidatedAfterExpiration = canBeValidatedAfterExpiration
            }
        }
        
    let toHttpResponseMessage mapRequestProperties resp =
        let output = new HttpResponseMessage ()
        
        output.Version <- resp.Version
        output.StatusCode <- resp.StatusCode
        output.ReasonPhrase <- resp.ReasonPhrase
        output.Content <-
            resp.Content 
            |> Option.map CachedContent.toHttpContent 
            |> Option.defaultValue null;
        output.RequestMessage <- CachedRequest.toHttpRequestMessage mapRequestProperties resp.Request
        
        for h in resp.Headers do output.Headers.Add(h.Key, h.Value)
        
        output