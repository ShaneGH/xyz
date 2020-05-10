module ShinyHttpCache.Tests.Utils.Mock

open System
open System.IO
open System.Net.Http
open System.Threading
open ShinyHttpCache.Dependencies
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
        cases: (Guid * MockCase<'a, 'b> * int) list
    }

let private addCase m case =
    let id = Guid.NewGuid()
    let newMock = { m with cases = (id, case, 0) :: m.cases }
    (id, newMock)

let private execute x m =
    let result =
        m.cases
        |> Seq.map (fun (id, c, y) -> ((id, c, y), c.verify x))
        |> Seq.filter (fun (_, v) -> v)
        |> Seq.map (fun (x, _) -> x)
        |> Seq.tryHead
        
    match result, m.strict with
    | Some (id, executed, count), _ ->
        let cases = m.cases |> List.map(fun (i, ex, c) -> if i = id then (id, executed, count + 1) else (id, ex, c))
        let mock = { m with cases = cases }
        (mock, executed.execute x)
    | None, true -> sprintf "Method %s has not been mocked" m.name |> invalidOp
    | None, false -> (m, Unchecked.defaultof<'b>)
    
type ICachingHttpClientDependenciesMethods =
    {
        get: Mock<string, Stream option Async>
        put: Mock<(string * Unit * Stream), Unit Async>
        delete: Mock<string, Unit Async>
        buildUserKey: Mock<CachedRequest, string option>
        send: Mock<(HttpRequestMessage * CancellationToken), HttpResponseMessage Async>
    }

[<RequireQualifiedAccess>]
module Mock =
    let private mockMethod getMethod setMethod fVerify fExecute mock =
        let (id, newCases) = addCase (getMethod mock) { verify = fVerify; execute = fExecute }
        let mock = setMethod mock newCases
        (id, mock)
        
    let newMock strict =
        {
            get = { strict = strict; cases = []; name = "get" }
            put = { strict = strict; cases = []; name = "put" }
            delete = { strict = strict; cases = []; name = "delete" }
            buildUserKey = { strict = strict; cases = []; name = "buildUserKey" }
            send = { strict = strict; cases = []; name = "send" }
        }
        
    let get = mockMethod (fun x -> x.get) (fun x y -> { x with get = y })
    let put = mockMethod (fun x -> x.put) (fun x y -> { x with put = y })
    let delete = mockMethod (fun x -> x.delete) (fun x y -> { x with delete = y })
    let buildUserKey = mockMethod (fun x -> x.buildUserKey) (fun x y -> { x with buildUserKey = y })
    let send = mockMethod (fun x -> x.send) (fun x y -> { x with send = y })
     
type MutableMocks = 
    {
        mutable CacheMethods: ICachingHttpClientDependenciesMethods
        lock: obj
    }

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