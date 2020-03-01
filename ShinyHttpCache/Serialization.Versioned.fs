module ShinyHttpCache.Serialization.Versioned
open ShinyHttpCache.Utils.Disposables
open ShinyHttpCache.Serialization
open System.IO
open System

module private Private =
    let asyncMap f x = async { 
        let! x1 = x; 
        return (f x1) 
    }
    
    let asyncBind f x = async { 
        let! x1 = x; 
        return! (f x1) 
    }

    let getVersion (s: Stream) =
        match s.Length with
        | x when x < 4L -> sprintf "This data was not created by ShinyHttpCache (stream.Length = %d)" x |> invalidOp
        | _ ->
            let bytes = Array.create 4 Unchecked.defaultof<byte>
            s.ReadAsync(bytes, 0, 4)
            |> Async.AwaitTask
            |> asyncMap (fun _ ->
                let major = System.BitConverter.ToUInt16(bytes, 0)
                let minor = System.BitConverter.ToUInt16(bytes, 2)
                ((major, minor), s))

    let getDerializer = function
        | (1us, 0us)
        | (1us, _) -> 
            Deserailizers.V1.deserialize
            >> asyncMap Dtos.V1.fromDto
        // TODO: handle more gracefully (warn and return cache miss rather than throw)
        // TODO: better message, include current dll version + supported serializer versions
        | (major, minor) -> sprintf "Invalid serialized version %d.%d" major minor |> invalidOp

    let prependVersion (major: uint16, minor: uint16) (s: Disposables<Stream>) =
        let ms = new MemoryStream() :> Stream
        let str = buildFromDisposable ms []
        let v = Array.concat [|BitConverter.GetBytes major;BitConverter.GetBytes minor|]

        ms.AsyncWrite(v, 0, 4)
        |> asyncBind (fun _ ->
            streamAsync (fun (s: Stream) -> s.CopyToAsync(ms)) s)
        |> asyncBind (fun _ ->
            streamAsync (fun (s: Stream) -> s.FlushAsync()) s)
        |> asyncMap (fun _ ->
            ms.Position <- 0L
            combine str s)

open Private

// Structure:
//  version length bytes (int), version, data

let serialize x =
    Dtos.Latest.toDto x
    |> asyncBind Serializer.serialize
    |> asyncBind (fun (v, stream) -> prependVersion v stream)

let deserialize s =
    getVersion s
    |> asyncBind (fun (v, s) -> getDerializer v s)