module ShinyHttpCache.Cache

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http

module Utils =
    let toAsync x =
        async {
            return x
        }
        
    let asyncBind (f: 'a -> Async<'b>) x =
        async {
            let! x1 = x 
            return! f x1
        }
        
    let asyncMap (f: 'a -> 'b) x =
        async {
            let! x1 = x 
            return f x1
        }
        
    module State =

        type State<'s,'a> = State of ('s-> Async<('s*'a)>)
        
        let get = State(fun s -> (s, s) |> toAsync)

        let unwrap = function | State x -> x

        let retn<'s,'x> (x: 'x) =
            let newAction (s: 's) =  toAsync (s,x)
            newAction |> State

        let run state (State action) =
            action state

        let map f action =
            let newAction state =
                async {
                    let! (s,x) = run state action
                    return (s,f x)
                }

            State newAction

        let bind f xAction =
            fun state ->
                async {
                    let! (s1, x1) = run state xAction
                    return!  f x1 s1
                }
            |> State
        
        type Workflow() =
            member __.Bind(s, f) = bind f s
            member __.Return x = retn x
            member __.ReturnFrom v = v
        
    let state = new State.Workflow()
    
type CacheTime =
    | AbsoluteValueUtc of DateTime
    | TimeFrame of TimeSpan
    
type CacheArgs =
    {
        Key: string
        CacheTime: CacheTime
    }
    
let buildCacheKey (req: HttpRequestMessage) = req.RequestUri.ToString()
    
let buildCacheTime (req: HttpResponseMessage) = TimeSpan.FromMinutes 30.0 |> TimeFrame
    
let buildCacheArgs (req: HttpResponseMessage) =
    {
        Key = buildCacheKey req.RequestMessage
        CacheTime = buildCacheTime req
    } 
   
type UnderlyingCache<'a> =
    {
        CacheResponse: string -> 'a -> CachedResponse.CachedResponse -> Async<('a * bool)>
        TryGetCachedResponse:  string -> 'a -> Async<('a * CachedResponse.CachedResponse option)>
        ShouldCacheRequest:  HttpRequestMessage -> bool
        BuildCacheKey:  HttpRequestMessage -> string
    }
        
let private allowedMethods = [HttpMethod.Get; HttpMethod.Head; HttpMethod.Options]
let buildDefaultState initialState =
    {
        CacheResponse = fun _ _ _ -> Utils.toAsync (initialState, false);
        TryGetCachedResponse = fun _ _ -> Utils.toAsync (initialState, option.None)
        ShouldCacheRequest = fun x -> List.contains x.Method allowedMethods
        BuildCacheKey = fun x -> x.RequestUri.ToString()
    }
    

//let date = DateTime.ParseExact("Mon, 11 Nov 2019 08:36:00 GMT",
//                    "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
//                    System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat,
//                    System.Globalization.DateTimeStyles.AdjustToUniversal).Kind
//let date = new DateTime(date.Offset, DateTimeKind.Utc)
    
let addValue cache (res: HttpResponseMessage) =
    fun state ->
        match cache.ShouldCacheRequest res.RequestMessage with
        | false -> Utils.toAsync (state, false)
        | true ->
            let key = cache.BuildCacheKey res.RequestMessage
            
            CachedResponse.build res
            |> Utils.asyncBind (cache.CacheResponse key state)
    |> Utils.State.State
    
let tryGetValue<'a> cache req =
    fun (state: 'a) ->
        let key = cache.BuildCacheKey req
        cache.TryGetCachedResponse key state
    |> Utils.State.State