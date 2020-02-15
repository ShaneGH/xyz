module rec ShinyHttpCache.Streams
open System.IO
open System
open Private

module private Private =
    let asyncMap f x = async { 
        let! x1 = x; 
        return (f x1) 
    }

    let private getDisposables =
        let rec unwrap (disposable: IDisposable) =
            match disposable with
            | :? Streams as s ->
                match s with
                | Streams x -> 
                    x.Disposables
                    |> List.map unwrap
                    |> List.fold (fun s x -> List.concat [s; x]) []
            | (x: IDisposable) -> [x]

        let getStream = function | Streams x -> x.Stream :> IDisposable

        unwrap >> List.distinct

    let disposeOfStreams = 
        getDisposables
        >> List.map (fun x -> x.Dispose())
        >> ignore

    type StreamsValues =
        { 
            Stream: Stream
            Disposables: IDisposable list
        }

type Streams = 
    private | Streams of StreamsValues 
    interface IDisposable with
        member x.Dispose() = disposeOfStreams x

let build (s: Stream, disposeOfStream) (ss: IDisposable list) =
    let ss = if disposeOfStream then List.concat [ss; [s]] else ss
    {
        Stream = s
        Disposables = ss
    }
    |> Streams

let getStream = function | Streams x -> x.Stream

let combine primary (secondary: Streams) =
    match primary with
    | Streams x -> 
        List.concat [x.Disposables; [secondary]]
        |> build (x.Stream, false) 