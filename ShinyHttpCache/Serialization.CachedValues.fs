module ShinyHttpCache.Serialization.CachedValues.Root
open ShinyHttpCache.Streams
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
        | (1us, 0us) -> V1.deserialize
        | (1us, _) -> V1.deserialize
        // TODO: handle more gracefully
        // TODO: better message, include current dll version + supported serializer versions
        | (major, minor) -> sprintf "Invalid serialized version %d.%d" major minor |> invalidOp

    let prependVersion (major: uint16, minor: uint16) (s: Streams) =
        let ms = new MemoryStream()
        let str = build (ms, true) []
        let v = Array.concat [|BitConverter.GetBytes major;BitConverter.GetBytes minor|]

        ms.AsyncWrite(v, 0, 4)
        |> asyncBind (fun _ ->
            streamAsync (fun s -> s.CopyToAsync(ms)) s)
        |> asyncBind (fun _ ->
            streamAsync (fun s -> s.FlushAsync()) s)
        |> asyncMap (fun _ ->
            ms.Position <- 0L
            combine str s)

open Private

// Structure:
//  version length bytes (int), version, data

let serialize x =
    Dtos.toDto x
    |> CachedValues.Serializer.serialize
    |> asyncBind (fun (v, stream) -> prependVersion v stream)

let deserialize s =
    getVersion s
    |> asyncBind (fun (v, s) -> getDerializer v s)
    |> asyncMap Dtos.fromDto