module ShinyHttpCache.Tests.Utils.Mock

open System
open System.IO
open System.Net.Http
open System.Threading
open ShinyHttpCace.Utils
open ShinyHttpCache
open ShinyHttpCache.Dependencies
open ShinyHttpCache.Model
open ShinyHttpCache.Serialization.HttpResponseValues

type MockCase<'a, 'b> =
    {
        verify: 'a -> bool
        execute: 'a -> 'b
    }

type Mock<'a, 'b> =
    {
        strict: bool
        name: string
        cases: (Guid * MockCase<'a, 'b>) list
        calls: 'a list
    }

let private addCase m case =
    let id = Guid.NewGuid()
    let newMock = { m with cases = (id, case) :: m.cases }
    (id, newMock)

let private execute x m =
    let m = { m with calls = x :: m.calls |> List.rev }
    let result =
        m.cases
        |> Seq.map (fun (id, c) -> ((id, c), c.verify x))
        |> Seq.filter (fun (_, v) -> v)
        |> Seq.map (fun (x, _) -> x)
        |> Seq.tryHead
        
    match result, m.strict with
    | Some (id, executed), _ ->
        let cases = m.cases |> List.map(fun (i, ex) -> if i = id then (id, executed) else (id, ex))
        let mock = { m with cases = cases }
        (mock, executed.execute x)
    | None, true -> sprintf "Method %s has not been mocked" m.name |> invalidOp
    | None, false -> (m, Unchecked.defaultof<'b>)
    
type ICachingHttpClientDependenciesMethods =
    {
        get: Mock<string, Stream option Async>
        put: Mock<(string * Stream * Dependencies.CacheMetadata), Unit Async>
        delete: Mock<string, Unit Async>
        buildUserKey: Mock<CachedRequest, string option>
        send: Mock<(HttpRequestMessage * CancellationToken), HttpResponseMessage Async>
    }
    
type MutableMocks = 
    {
        mutable CacheMethods: ICachingHttpClientDependenciesMethods
        lock: obj
    }

[<RequireQualifiedAccess>]
module Mock =
    let private mockMethod getMethod setMethod fVerify fExecute mock =
        let (id, newCases) = addCase (getMethod mock) { verify = fVerify; execute = fExecute }
        let mock = setMethod mock newCases
        (id, mock)
        
    let newMock strict =
        {
            get = { strict = strict; cases = []; name = "get"; calls = [] }
            put = { strict = strict; cases = []; name = "put"; calls = [] }
            delete = { strict = strict; cases = []; name = "delete"; calls = [] }
            buildUserKey = { strict = strict; cases = []; name = "buildUserKey"; calls = [] }
            send = { strict = strict; cases = []; name = "send"; calls = [] }
        }
        
    let get = mockMethod (fun x -> x.get) (fun x y -> { x with get = y })
    let put = mockMethod (fun x -> x.put) (fun x y -> { x with put = y })
    let delete = mockMethod (fun x -> x.delete) (fun x y -> { x with delete = y })
    let buildUserKey = mockMethod (fun x -> x.buildUserKey) (fun x y -> { x with buildUserKey = y })
    let send = mockMethod (fun x -> x.send) (fun x y -> { x with send = y })
    
    let private verifyAsync getter f mock =
        mock.CacheMethods
        |> getter
        |> (fun x -> x.calls)
        |> List.map f
        |> Async.Parallel
        |> Infra.Async.map (Array.filter id)
        |> Infra.Async.map (Array.length)
    
    let private verify getter f mock =
        mock.CacheMethods
        |> getter
        |> (fun x -> x.calls)
        |> List.filter f
        |> List.length
        
    let verifyGet = verify (fun x -> x.get)
    let verifyPut = verify (fun x -> x.put)
    let verifyPutAsync = verifyAsync (fun x -> x.put)
    let verifyDelete = verify (fun x -> x.delete)
    let verifyBuildUserKey = verify (fun x -> x.buildUserKey)
    let verifySend = verify (fun x -> x.send)

type private Cache = 
    {
        CacheMethods: MutableMocks
    }
    
    interface ICache with
        member this.Get key =
            let (mock, result) = execute key this.CacheMethods.CacheMethods.get
            lock this.CacheMethods.lock (fun _ -> this.CacheMethods.CacheMethods <- { this.CacheMethods.CacheMethods with get = mock })
            result
        member this.Put key value serializedValue =
            let (mock, result) = execute (key, value, serializedValue) this.CacheMethods.CacheMethods.put
            lock this.CacheMethods.lock (fun _ -> this.CacheMethods.CacheMethods <- { this.CacheMethods.CacheMethods with put = mock })
            result
        member this.Delete key =
            let (mock, result) = execute key this.CacheMethods.CacheMethods.delete
            lock this.CacheMethods.lock (fun _ -> this.CacheMethods.CacheMethods <- { this.CacheMethods.CacheMethods with delete = mock })
            result
        member this.BuildUserKey req =
            let (mock, result) = execute req this.CacheMethods.CacheMethods.buildUserKey
            lock this.CacheMethods.lock (fun _ -> this.CacheMethods.CacheMethods <- { this.CacheMethods.CacheMethods with buildUserKey = mock })
            result
            
type private CachingHttpClientDependencies = 
    {
        CacheMethods: MutableMocks
        ICacheMethods: ICache
    }
    
    interface ICachingHttpClientDependencies with
        member this.Send req =
            let (mock, result) = execute req this.CacheMethods.CacheMethods.send
            lock this.CacheMethods.lock (fun _ -> this.CacheMethods.CacheMethods <- { this.CacheMethods.CacheMethods with send = mock })
            result
            
        member this.Cache = this.ICacheMethods
        
let object (mock: ICachingHttpClientDependenciesMethods) =
    let mutableCache = { CacheMethods = mock; lock = obj() }
    let mock = {
        CacheMethods = mutableCache
        ICacheMethods =
            {
                CacheMethods = mutableCache
            }: Cache
    }
    
    (mutableCache, mock :> ICachingHttpClientDependencies)