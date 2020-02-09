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

let compress stream = new GZipStream(stream, CompressionMode.Compress) :> Stream

let decompress stream = new GZipStream(stream, CompressionMode.Decompress) :> Stream

let serializeCompressed x = (serialize >> compress) x

let deserializeDecompressed x = (decompress >> deserailize) x