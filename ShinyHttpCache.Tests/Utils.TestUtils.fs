module ShinyHttpCache.Tests.Utils.TestUtils
open System.Threading
open System
open System.Net.Http
open System.Threading.Tasks
open Moq
open ShinyHttpCache.FSharp.CachingHttpClient
open ShinyHttpCache.Utils.ReaderMonad

module ExecuteRequestArgs =
    type Args =
        {
            url: string
            user: string option
        }
        
    let value =
        {
            url = "http://www.com"
            user = None
        }
        
    let setUrl url x = { x with url = url  }
    let setUser user x = { x with user = user  }
open ExecuteRequestArgs
    
let executeRequest args (dependencies: Mock<ICachingHttpClientDependencies>) =
    let request = new HttpRequestMessage()
    request.RequestUri <- Uri(args.url)
    
    match args.user with
    | Some x -> request.Headers.Add(TestState.UserHeader, x)
    | None -> ()

    let reader = client(request, Unchecked.defaultof<CancellationToken>)
    Reader.run dependencies.Object reader
    
let asTask x = Async.StartAsTask x |> (fun x -> x :> Task)