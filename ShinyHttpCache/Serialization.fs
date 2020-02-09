module ShinyHttpCache.Serialization
open System.Runtime.Serialization.Formatters.Binary
open System.IO
open System.IO.Compression

// TODO: is this thread safe?
let private serializer = BinaryFormatter();
let serialize value =
    
    let ms = new MemoryStream()
    serializer.Serialize(ms, value)
    ms.Position <- 0L
    ms :> Stream

let deserailize<'a> stream =
    serializer.Deserialize(stream) :?> 'a

let compress (stream: Stream) = 
    let ms = new MemoryStream()

    async {
        use gz = new GZipStream(ms, CompressionMode.Compress)
        do! stream.CopyToAsync(gz) |> Async.AwaitTask
        ms.Position <- 0L
        return (ms :> Stream)
    }

let decompress (stream: Stream) = 
    let ms = new MemoryStream()

    async {
        use gz = new GZipStream(ms, CompressionMode.Decompress)
        do! stream.CopyToAsync(gz) |> Async.AwaitTask
        ms.Position <- 0L
        return (ms :> Stream)
    }

let serializeCompressed x = 
    async {
        use ser = serialize x
        return! compress ser
    }

let deserializeDecompressed x =
    async {
        use! dec = decompress x
        return deserailize dec
    }

let compress2 (stream: Stream) = 
    let ms = new MemoryStream()
    
    let gz = new GZipStream(ms, CompressionMode.Compress)
    do stream.CopyTo(gz)
    ms.Position <- 0L
    (ms :> Stream)

let serializeCompressed2 x = 
    use ser = serialize x
    compress2 ser