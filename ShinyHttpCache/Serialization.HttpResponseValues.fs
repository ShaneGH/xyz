module ShinyHttpCache.Serialization.HttpResponseValues

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open ShinyHttpCache.Utils
open ShinyHttpCache.Utils

module private Private = 
    let toCSharpList (seq: 'a seq) = List<'a> seq

    let asyncReturn x = async { return x }
    
    let asyncMap f x = async { 
        let! x1 = x
        return (f x1) 
    }

    let mapHeaderKvp (kvp: KeyValuePair<string, IEnumerable<'a>>) =
        new KeyValuePair<string, List<'a>>(kvp.Key, toCSharpList kvp.Value)

    let toOption = function
        | x when isNull x -> None
        | x -> Some x

    let invertOpt = function
        | None -> asyncReturn None
        | Some x -> asyncMap Some x

open Private

type CachedHttpContent (content: byte[], headers: KeyValuePair<string, List<string>> seq) as this =
    inherit HttpContent()
    do for header in headers do this.Headers.Add(header.Key, header.Value)
    
    override __.SerializeToStreamAsync (stream, _) =
        stream.WriteAsync (content, 0, content.Length)
    
    override __.TryComputeLength (length) =
        length <- content.LongLength
        true

type CachedContent =
    {
        Headers: List<KeyValuePair<string, List<string>>>
        Content: byte array
    }
    
let buildCachedContent (content: HttpContent) =
    async {
        let! c = content.ReadAsByteArrayAsync() |> Async.AwaitTask
        return {
            Headers = content.Headers
               |> Seq.map mapHeaderKvp
               |> toCSharpList
            Content = c
        }
    }
    
let toHttpContent cachedContent =
    new CachedHttpContent (cachedContent.Content, cachedContent.Headers |> toCSharpList)
    :> HttpContent
    
type CachedRequest =
    {
        Version: List<int>;
        Method: string;
        Uri: Uri;
        Content: CachedContent option;
        Headers: List<KeyValuePair<string, List<string>>>;
    }
    
   // TODO: currently this method is called a few times
   // try to get this down to once
let buildCachedRequest (req: HttpRequestMessage) =
    req.Content 
    |> toOption
    |> Option.map buildCachedContent
    |> invertOpt
    |> asyncMap (fun c -> {
        Version = SerializableVersion.fromSemanticVersion req.Version;
        Method = req.Method.Method;
        Uri = req.RequestUri;
        Content = c;
        Headers = req.Headers
            |> Seq.map mapHeaderKvp
            |> toCSharpList;
        // TODO: introduce properties? 
        //Properties = req.Properties |> List.ofSeq;
    })
    
let toHttpRequestMessage req =
    let output = new HttpRequestMessage ()
    
    output.Version <- SerializableVersion.toSemanticVersion req.Version
    output.Method <- HttpMethod(req.Method)
    output.RequestUri <- req.Uri
    output.Content <- 
        req.Content 
        |> Option.map toHttpContent 
        |> Option.defaultValue null;
    
    for h in req.Headers do output.Headers.Add(h.Key, h.Value)
    
    output
    
type CachedResponse =
    {
        Version: List<int>
        StatusCode: HttpStatusCode
        ReasonPhrase: string
        Content: CachedContent option
        Request: CachedRequest
        Headers: List<KeyValuePair<string, List<string>>>
    }
    
let buildCachedResponse (resp: HttpResponseMessage) =
    let content = 
        resp.Content 
        |> toOption 
        |> Option.map buildCachedContent 
        |> invertOpt

    let req = buildCachedRequest resp.RequestMessage
        
    async {
        let! c' = content
        let! r' = req

        return {
            Version = SerializableVersion.fromSemanticVersion resp.Version;
            StatusCode = resp.StatusCode;
            ReasonPhrase = resp.ReasonPhrase;
            Content = c';
            Headers = resp.Headers 
               |> Seq.map mapHeaderKvp
               |> toCSharpList;
            Request = r'
        }
    }
    
let toHttpResponseMessage resp =
    let output = new HttpResponseMessage ()
    
    output.Version <- SerializableVersion.toSemanticVersion  resp.Version
    output.StatusCode <- resp.StatusCode
    output.ReasonPhrase <- resp.ReasonPhrase
    output.Content <-
        resp.Content 
        |> Option.map toHttpContent 
        |> Option.defaultValue null;
    output.RequestMessage <- toHttpRequestMessage resp.Request
    
    for h in resp.Headers do output.Headers.Add(h.Key, h.Value)
    
    output