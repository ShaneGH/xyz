module ShinyHttpCache.Serialization.Versioned
open ShinyHttpCace.Utils
open ShinyHttpCache.Utils.Disposables
open ShinyHttpCache.Serialization
open System.IO
open System

module private Private =

    let getVersion (s: Stream) =
        match s.Length with
        | x when x < 4L -> sprintf "This data was not created by ShinyHttpCache (stream.Length = %d)" x |> invalidOp
        | _ ->
            let bytes = Array.create 4 Unchecked.defaultof<byte>
            s.ReadAsync(bytes, 0, 4)
            |> Async.AwaitTask
            |> Infra.Async.map (fun _ ->
                let major = System.BitConverter.ToUInt16(bytes, 0)
                let minor = System.BitConverter.ToUInt16(bytes, 2)
                ((major, minor), s))

    let getDeserializer = function
        | (1us, 0us)
        | (1us, _) -> 
            Deserailizers.V1.deserialize
            >> Infra.Async.map Dtos.V1.fromDto
        // TODO: handle more gracefully (warn and return cache miss rather than throw)
        // TODO: better message, include current dll version + supported serializer versions
        | (major, minor) -> sprintf "Invalid serialized version %d.%d" major minor |> invalidOp

    let prependVersion (major: uint16, minor: uint16) (s: Disposables<Stream>) =
        let ms = new MemoryStream() :> Stream
        let str = buildFromDisposable ms []
        let v = Array.concat [|BitConverter.GetBytes major;BitConverter.GetBytes minor|]

        ms.AsyncWrite(v, 0, 4)
        |> Infra.Async.bind (fun _ ->
            streamAsync (fun (s: Stream) -> s.CopyToAsync(ms)) s)
        |> Infra.Async.bind (fun _ ->
            streamAsync (fun (s: Stream) -> s.FlushAsync()) s)
        |> Infra.Async.map (fun _ ->
            ms.Position <- 0L
            combine str s)

open Private

// Structure:
//  version length bytes (int), version, data

let serialize =
    Serializer.serialize
    >> Infra.Async.bind (fun (v, stream) -> prependVersion v stream)

let deserialize s =
    getVersion s
    |> Infra.Async.bind (fun (v, s) -> getDeserializer v s)