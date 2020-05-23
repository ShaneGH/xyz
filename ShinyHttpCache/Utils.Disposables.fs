module rec ShinyHttpCache.Utils.Disposables
open System
open Private
open System.Threading.Tasks

module private Private =

    let private getDisposables: IDisposables -> IDisposable list =
        let rec unwrap (disposable: IDisposable) =
            match disposable with
            | :? IDisposables as s ->
                s.GetDisposables()
                |> List.map unwrap
                |> List.fold (fun s x -> List.concat [s; x]) []
            | (x: IDisposable) -> [x]

        unwrap >> List.distinct

    let disposeOfValues: IDisposables -> unit = 
        getDisposables
        >> List.map (fun x -> x.Dispose())
        >> ignore

    type Values<'a> =
        { 
            Value: 'a
            Disposables: IDisposable list
        }

type IDisposables =
    inherit IDisposable
    abstract member GetDisposables: unit -> IDisposable list

type Disposables<'a> = 
    private | Disposables of Values<'a> 
    interface IDisposable with
        member x.Dispose() = disposeOfValues x
    interface IDisposables with
        member x.GetDisposables() = 
            match x with
            | Disposables y -> y.Disposables

let build v disposables =
    {
        Value = v
        Disposables = disposables
    }
    |> Disposables

let buildFromDisposable (v: 'a) (disposables: IDisposable list) =
    List.concat [disposables; [v]] 
    |> List.distinct
    |> build v

let getValue = function | Disposables x -> x.Value

let value f = getValue >> f

let streamAsync (f: 'a -> Task) = getValue >> f >> Async.AwaitTask

let combine primary (secondary: Disposables<'a>) =
    match primary with
    | Disposables x -> 
        List.concat [x.Disposables; [secondary]]
        |> build x.Value 