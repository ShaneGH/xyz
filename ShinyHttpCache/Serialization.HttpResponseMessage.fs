﻿namespace ShinyHttpCache.Serialization.HttpResponseMessage

open System
open System.Collections.Generic
open System.Net
open System.Net.Http

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

module CachedContent =

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
        
    let build (content: HttpContent) =
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

module CachedRequest =
    
    type CachedRequest =
        {
            Version: Version;
            Method: string;
            Uri: Uri;
            Content: CachedContent.CachedContent option;
            Headers: List<KeyValuePair<string, List<string>>>;
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
        
        output.Version <- req.Version
        output.Method <- HttpMethod(req.Method)
        output.RequestUri <- req.Uri
        output.Content <- 
            req.Content 
            |> Option.map CachedContent.toHttpContent 
            |> Option.defaultValue null;
        
        for h in req.Headers do output.Headers.Add(h.Key, h.Value)
        
        output

module CachedResponse =
    
    type CachedResponse =
        {
            Version: Version
            StatusCode: HttpStatusCode
            ReasonPhrase: string
            Content: CachedContent.CachedContent option
            Request: CachedRequest.CachedRequest
            Headers: List<KeyValuePair<string, List<string>>>
        }
        
    let build (resp: HttpResponseMessage) =
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
                   |> toCSharpList;
                Request = r'
            }
        }
        
    let toHttpResponseMessage resp =
        let output = new HttpResponseMessage ()
        
        output.Version <- resp.Version
        output.StatusCode <- resp.StatusCode
        output.ReasonPhrase <- resp.ReasonPhrase
        output.Content <-
            resp.Content 
            |> Option.map CachedContent.toHttpContent 
            |> Option.defaultValue null;
        output.RequestMessage <- CachedRequest.toHttpRequestMessage resp.Request
        
        for h in resp.Headers do output.Headers.Add(h.Key, h.Value)
        
        output