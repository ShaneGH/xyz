module ShinyHttpCache.Dependencies

open System.IO
open System.Net.Http
open System.Threading
open ShinyHttpCache.Serialization.HttpResponseValues

type ICache =
    abstract member Get : key: string -> Stream option Async
    //TODO: replace Unit with the unserialized version of the stream
    abstract member Put : key: string -> cacheData: Unit -> serializedCacheData: Stream -> unit Async
    abstract member Delete : key: string -> unit Async
    abstract member BuildUserKey : CachedRequest -> string option

type ICachingHttpClientDependencies =
    abstract member Cache : ICache
    abstract member Send : (HttpRequestMessage * CancellationToken) -> HttpResponseMessage Async

type CachingHttpClientDependencies = private CachingHttpClientDependencies of ICachingHttpClientDependencies

let create (x: ICachingHttpClientDependencies) =
    match x with
    | x when (x :> obj) = null -> invalidOp "The ICachingHttpClientDependencies cannot be null"
    | x when (x.Cache :> obj) = null -> invalidOp "The ICache cannot be null"
    | _ -> ()
    
    CachingHttpClientDependencies x

let send cache req c =
    match cache with
    | CachingHttpClientDependencies x -> async {
        let! result = x.Send (req, c)
        
        let result =
            match result with
            | null -> invalidOp "Send must return a value"
            | x -> x
            
        return result
    }
    
let get cache key =
    match cache with
    | CachingHttpClientDependencies x -> async {
        let! result = x.Cache.Get key
        
        let result =
            match result with
            | Some null -> None
            | x -> x
            
        return result
    }
    
let put cache key cacheData serializedCacheData =
    match cache with
    | CachingHttpClientDependencies x -> async {
        let! _ = x.Cache.Put key cacheData serializedCacheData
        return ()
    }
    
let delete cache req =
    match cache with
    | CachingHttpClientDependencies x -> async {
        let! _ = x.Cache.Delete req
        return ()
    }
    
let buildUserKey cache req =
    match cache with
    | CachingHttpClientDependencies x -> 
        match x.Cache.BuildUserKey req with
        | Some null -> None
        | x -> x