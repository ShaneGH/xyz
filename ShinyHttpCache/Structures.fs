namespace ShinyHttpCache

open System
open System.Collections.Generic
open System.Net
open System.Net.Http

module private Utils =
    let mapHeaderKvp (kvp: KeyValuePair<string, IEnumerable<'a>>) =
        new KeyValuePair<string, 'a list>(kvp.Key, List.ofSeq kvp.Value)
open System.Text
open Utils

module CachedContent =

    type CachedHttpContent (content: byte[], headers: KeyValuePair<string, string list> seq) as this =
        inherit HttpContent()
        do for header in headers do this.Headers.Add(header.Key, header.Value)
        
        override this.SerializeToStreamAsync (stream, _) =
            stream.WriteAsync (content, 0, content.Length)
        
        override this.TryComputeLength (length) =
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
        
    let getContentLength cachedContent =
        let contentLength = cachedContent.Content.Length + 8
        let headerLength =
            cachedContent.Headers
            |> List.map (fun x -> Encoding.UTF8.)

module CachedRequest =
    
    type CachedRequest =
        {
            Version: Version;
            Method: HttpMethod;
            Uri: Uri;
            Content: CachedContent.CachedContent;
            Headers: KeyValuePair<string, string list> list;
            Properties: KeyValuePair<string, obj> list;
        }
        
    let build (req: HttpRequestMessage) =
        async {
            let! c = CachedContent.build req.Content
            return {
                Version = req.Version;
                Method = req.Method;
                Uri = req.RequestUri;
                Content = c;
                Headers = req.Headers
                   |> Seq.map mapHeaderKvp
                   |> List.ofSeq;
                Properties = req.Properties |> List.ofSeq;
            }
        }
        
    let toHttpRequestMessage mapProperties req =
        let output = new HttpRequestMessage ()
        
        output.Version <- req.Version
        output.Method <- req.Method
        output.RequestUri <- req.Uri
        output.Content <- CachedContent.toHttpContent req.Content;
        
        for h in req.Headers do output.Headers.Add(h.Key, h.Value)
        
        if mapProperties then
            for p in req.Properties do output.Properties.Add(p.Key, p.Value)
        
        output

module CachedResponse =
    
    type CachedResponse =
        {
            Version: Version;
            StatusCode: HttpStatusCode;
            ReasonPhrase: string;
            Content: CachedContent.CachedContent;
            Request: CachedRequest.CachedRequest;
            Headers: KeyValuePair<string, string list> list;
        }
        
    let build (resp: HttpResponseMessage) =
        let c = CachedContent.build resp.Content
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
            }
        }
        
    let toHttpResponseMessage mapRequestProperties resp =
        let output = new HttpResponseMessage ()
        
        output.Version <- resp.Version
        output.StatusCode <- resp.StatusCode
        output.ReasonPhrase <- resp.ReasonPhrase
        output.Content <- CachedContent.toHttpContent resp.Content;
        output.RequestMessage <- CachedRequest.toHttpRequestMessage mapRequestProperties resp.Request;
        
        for h in resp.Headers do output.Headers.Add(h.Key, h.Value)
        
        output