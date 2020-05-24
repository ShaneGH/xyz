module ShinyHttpCache.Utils.CompressedSerialization
open ShinyHttpCace.Utils
open ShinyHttpCache.Utils
open System.IO
open System.IO.Compression
open System.Text.Json
open System.Threading

module private Private =
    let private gzip (mode: CompressionMode) (stream: Disposables.Disposables<Stream>) = 
        let ms = new MemoryStream()
        let gz = new GZipStream(ms, mode)
        let newStreams = Disposables.buildFromDisposable ms [gz]
        let data = Disposables.getValue stream

        async {
            do! data.CopyToAsync(gz) |> Async.AwaitTask
            do! gz.FlushAsync() |> Async.AwaitTask
            ms.Position <- 0L
            return Disposables.combine newStreams stream
        }

    let compress (stream: Disposables.Disposables<Stream>) = 
        let ms = new MemoryStream() :> Stream
        let gz = new GZipStream(ms, CompressionMode.Compress)
        let newStreams = Disposables.buildFromDisposable ms [gz]
        let data = Disposables.getValue stream

        async {
            do! data.CopyToAsync(gz) |> Async.AwaitTask
            do! gz.FlushAsync() |> Async.AwaitTask
            ms.Position <- 0L
            return Disposables.combine newStreams stream
        }

    let decompress (stream: Disposables.Disposables<Stream>) = 
        let ms = new MemoryStream() :> Stream
        let data = Disposables.getValue stream
        let gz = new GZipStream(data, CompressionMode.Decompress)
        let newStreams = Disposables.buildFromDisposable ms [gz]

        async {
            do! gz.CopyToAsync(ms) |> Async.AwaitTask
            do! gz.FlushAsync() |> Async.AwaitTask
            ms.Position <- 0L
            return Disposables.combine newStreams stream
        }

open Private

let private serializationOptions =
    // TODO: there is caching that can be done here
    let options = JsonSerializerOptions()
    options.IgnoreNullValues <- true
    
    options.Converters.Add(SerializationConverters.RecordType.Factory())
    options.Converters.Add(SerializationConverters.Option.Factory())
    options

let serialize<'a> (dto: 'a) =
    let ms = new MemoryStream() :> Stream
    JsonSerializer.SerializeAsync(ms, dto, typedefof<'a>, serializationOptions, Unchecked.defaultof<CancellationToken>)
    |> Async.AwaitTask
    |> Infra.Async.map (fun _ ->
        ms.Position <- 0L
        Disposables.buildFromDisposable ms [])
    |> Infra.Async.bind compress

let deserialize<'a> s =

    async {
        use! str = Disposables.build s [] |> decompress
        
        let s = Disposables.getValue str
        let dtoT = JsonSerializer.DeserializeAsync<'a> (s, serializationOptions)

        let! dto = dtoT.AsTask() |> Async.AwaitTask
        return dto
    }