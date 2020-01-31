namespace ShinyHttpCache
open System.Net.Http
open CachingHttpClient


type CacheHttpMessageHandler (cache: ICache) =
    inherit HttpClientHandler()
    interface ICachingHttpClientDependencies with
        member this.Send x = this.Send x
        member __.Cache = cache

    member private __.Send(req, token) =
        base.SendAsync(req, token)
        |> Async.AwaitTask
 
    override this.SendAsync(req, token) = 
    
        client (req, token)
        |> ReaderMonad.Reader.run (this :> ICachingHttpClientDependencies)
        |> Async.StartAsTask
