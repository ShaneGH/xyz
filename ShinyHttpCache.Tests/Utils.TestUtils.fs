module ShinyHttpCache.Tests.Utils.TestUtils
open System.Threading
open System
open System.Net.Http
open System.Threading.Tasks
open ShinyHttpCache.FSharp.CachingHttpClient
open ShinyHttpCache.Tests.Utils.Mock
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
    
let executeRequest args (state: ICachingHttpClientDependenciesMethods) =
    
    let request = new HttpRequestMessage()
    request.RequestUri <- Uri(args.url)
    
    match args.user with
    | Some x -> request.Headers.Add(TestState.UserHeader, x)
    | None -> ()
    
    let (recorder, dependencies) = object state

    let reader = client(request, Unchecked.defaultof<CancellationToken>)
    (recorder, Reader.run dependencies reader)
    
let asTask x = Async.StartAsTask x |> (fun x -> x :> Task)

let asNullable x = Nullable<'a>(x)

let asyncMap f x = async {
    let! x = x
    let y = f x
    return y
}