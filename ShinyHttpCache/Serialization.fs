module ShinyHttpCache.Serialization
open System.IO
open ShinyHttpCache.CachingHttpClient
open System
open System.Text.Json
open System.Threading
open System.IO.Compression

module Dtos =
    open ShinyHttpCache.Headers.CacheSettings

    type EntityTagDto() =
        [<DefaultValue>] val mutable Type: char
        [<DefaultValue>] val mutable Value: string

    let toEntityTagDto = function
        | Strong x ->
            {
                Value = x
                Type = 's'
            }
        | Weak x ->
            {
                Value = x
                Type = 'w'
            }

    type ValidatorDto =
        {
            Type: char
            ETag: EntityTagDto
            ExpirationDateUtc: Nullable<DateTime>
        }

    let toValidarotDto = function
        | ETag x ->
            {
                Type = 't'
                ETag = toEntityTagDto x
                ExpirationDateUtc = Nullable<DateTime>()
            }
        | ExpirationDateUtc x ->
            {
                Type = 'd'
                ETag = null :> obj :?> EntityTagDto
                ExpirationDateUtc = Nullable<DateTime> x
            }
        | Both (x, y) ->
            {
                Type = 'b'
                ETag = toEntityTagDto x
                ExpirationDateUtc = Nullable<DateTime> y
            }

    type RevalidationSettingsDto = 
        {
            MustRevalidateAtUtc: DateTime
            Validator: ValidatorDto
        }

    type ExpirySettingsDto =
        {
            Type: char
            Soft: RevalidationSettingsDto
            HardUtc: Nullable<DateTime>
        }

    let toExpirySettingsDto = function
        | NoExpiryDate ->
            {
                Soft = null :> obj :?> RevalidationSettingsDto
                HardUtc = Nullable<DateTime>()
                Type = 'n'
            }
        | HardUtc x -> 
            {
                Soft = null :> obj :?> RevalidationSettingsDto
                HardUtc = Nullable<DateTime> x
                Type = 'h'
            }
        | Soft x -> 
            {
                Soft = 
                    {
                        MustRevalidateAtUtc = x.MustRevalidateAtUtc
                        Validator = toValidarotDto x.Validator
                    }
                HardUtc = Nullable<DateTime>()
                Type = 's'
            }

    type CacheSettingsDto =
        {
            ExpirySettings: ExpirySettingsDto
            SharedCache: bool
        }

    type CacheValuesDto =
        {
            ShinyHttpCacheVersion: Version
            HttpResponse: CachedResponse.CachedResponse
            CacheSettings: CacheSettingsDto
        }

    let toCacheSettingsDto (x: Headers.CacheSettings.CacheSettings) = 
        {
            SharedCache = x.SharedCache
            ExpirySettings = toExpirySettingsDto x.ExpirySettings
        }

    let private version = (typedefof<CacheValuesDto>).Assembly.GetName().Version
    let toDto (x: CachedValues) = 
        {
            ShinyHttpCacheVersion = version
            HttpResponse = x.HttpResponse
            CacheSettings = toCacheSettingsDto x.CacheSettings
        }

module private Private =
    let asyncMap f x = async { 
        let! x1 = x; 
        return (f x1) 
    }
    
    let asyncBind f x = async { 
        let! x1 = x; 
        return! (f x1) 
    }

    let tDto = typedefof<Dtos.CacheValuesDto>

    let private gzip (mode: CompressionMode) (stream: Streams.Streams) = 
        let ms = new MemoryStream()
        let gz = new GZipStream(ms, mode)
        let newStreams = Streams.build (ms, true) [gz]
        let data = Streams.getStream stream

        async {
            do! data.CopyToAsync(gz) |> Async.AwaitTask
            do! gz.FlushAsync() |> Async.AwaitTask
            ms.Position <- 0L
            return Streams.combine newStreams stream
        }

    let compress (stream: Streams.Streams) = 
        let ms = new MemoryStream()
        let gz = new GZipStream(ms, CompressionMode.Compress)
        let newStreams = Streams.build (ms, true) [gz]
        let data = Streams.getStream stream

        async {
            do! data.CopyToAsync(gz) |> Async.AwaitTask
            do! gz.FlushAsync() |> Async.AwaitTask
            ms.Position <- 0L
            return Streams.combine newStreams stream
        }

    let decompress (stream: Streams.Streams) = 
        let ms = new MemoryStream()
        let data = Streams.getStream stream
        let gz = new GZipStream(data, CompressionMode.Decompress)
        let newStreams = Streams.build (ms, true) [gz]

        async {
            do! gz.CopyToAsync(ms) |> Async.AwaitTask
            do! gz.FlushAsync() |> Async.AwaitTask
            ms.Position <- 0L
            return Streams.combine newStreams stream
        }


open Private

let serialize x =
    let dto = Dtos.toDto x
    let ms = new MemoryStream()

    JsonSerializer.SerializeAsync(ms, dto, tDto, null, Unchecked.defaultof<CancellationToken>)
    |> Async.AwaitTask
    |> asyncMap (fun _ -> 
        ms.Position <- 0L
        Streams.build (ms, true) [])
    |> asyncBind compress

let deserialize<'a> (ms: MemoryStream) =
    async {
        use! str = Streams.build (ms, false) [] |> decompress
        
        let s = Streams.getStream str
        let dtoT = JsonSerializer.DeserializeAsync<Dtos.CacheValuesDto> s

        return! dtoT.AsTask() |> Async.AwaitTask
    }
// open System.Runtime.Serialization.Formatters.Binary
// open System.IO
// open System.IO.Compression
// open System.Text.Json
// open System.Threading

// module FSharpUnionTypes =
//     open Microsoft.FSharp.Reflection
//     open System
//     open System.Collections.Generic

//     let private isUnionType<'a> () =
//         typedefof<'a> |> FSharpType.IsUnion

//     type UnionConverter<'a> () =
//         inherit Serialization.JsonConverter<'a>()
//             static let isUnion = isUnionType<'a>()
//             override __.CanConvert typeToConvert =
//                 isUnion && (base.CanConvert typeToConvert)
                
//             override __.Read (reader, typeToConvert, options) =
//                 Unchecked.defaultof<'a>

//             override __.Write(writer, value, options) =
//                 ()



// // /// <summary>Determines whether the specified type can be converted.</summary>
// // /// <param name="typeToConvert">The type to compare against.</param>
// // /// <returns>
// // ///   <see langword="true" /> if the type can be converted; otherwise, <see langword="false" />.</returns>
// // public override bool CanConvert(Type typeToConvert)
// // {
// // 	throw null;
// // }

// // /// <summary>Reads and converts the JSON to type <typeparamref name="T" />.</summary>
// // /// <param name="reader">The reader.</param>
// // /// <param name="typeToConvert">The type to convert.</param>
// // /// <param name="options">An object that specifies serialization options to use.</param>
// // /// <returns>The converted value.</returns>
// // public abstract T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);

// // /// <summary>Writes a specified value as JSON.</summary>
// // /// <param name="writer">The writer to write to.</param>
// // /// <param name="value">The value to convert to JSON.</param>
// // /// <param name="options">An object that specifies serialization options to use.</param>
// // public abstract void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options);

// module private Private =
//     let asyncMap f x = async { 
//         let! x1 = x; 
//         return (f x1) 
//     }

//     type MJC () =
//         inherit Serialization.JsonConverter()

//     let jsonOptions =
//         let options = JsonSerializerOptions()
//         options.p
//         options
// open Private

// // TODO: is this thread safe?
// let private serializer = BinaryFormatter();
// let serialize value =
    
//     // let ms = new MemoryStream()
//     // serializer.Serialize(ms, value)
//     // ms.Position <- 0L
//     // async { return (ms :> Stream) }

//     let ms = new MemoryStream()
//     let options = JsonSerializerOptions()
    
//     options.Converters

//     JsonSerializer.SerializeAsync(ms, value, value.GetType(), null, Unchecked.defaultof<CancellationToken>)
//     |> Async.AwaitTask
//     |> asyncMap (fun _ -> 
//         ms.Position <- 0L
//         let reader = new StreamReader(ms)
//         let yyy = reader.ReadToEnd()
//         invalidOp(yyy))
//     |> asyncMap (fun _ -> 
//         ms.Position <- 0L
//         ms)

// let deserailize<'a> stream =
//     serializer.Deserialize(stream) :?> 'a

// let compress (stream: Stream) = 
//     let ms = new MemoryStream()

//     async {
//         use gz = new GZipStream(ms, CompressionMode.Compress)
//         do! stream.CopyToAsync(gz) |> Async.AwaitTask
//         ms.Position <- 0L
//         return (ms :> Stream)
//     }

// let decompress (stream: Stream) = 
//     let ms = new MemoryStream()

//     async {
//         use gz = new GZipStream(ms, CompressionMode.Decompress)
//         do! stream.CopyToAsync(gz) |> Async.AwaitTask
//         ms.Position <- 0L
//         return (ms :> Stream)
//     }

// let serializeCompressed x = 
//     async {
//         use! ser = serialize x
//         return! compress ser
//     }

// let deserializeDecompressed x =
//     async {
//         use! dec = decompress x
//         return deserailize dec
//     }

// let compress2 (stream: Stream) = 
//     let ms = new MemoryStream()
    
//     let gz = new GZipStream(ms, CompressionMode.Compress)
//     do stream.CopyTo(gz)
//     ms.Position <- 0L
//     (ms :> Stream)

// let serializeCompressed2 x = 
//     use ser = serialize x |> Async.RunSynchronously
//     compress2 ser