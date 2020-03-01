module ShinyHttpCache.Utils.CompressedSerialization
open Microsoft.FSharp.Reflection
open ShinyHttpCache.Utils
open System
open System.IO
open System.IO.Compression
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading

module Converters =

    module Private1 =
        let assertToken expected actual =
            match actual with
            | x when x = expected -> ()
            | _ -> sprintf "Invalid token type %A. Expecting %A." actual expected |> JsonException |> raise

        let getConstructorArgs (constructor: ConstructorInfo) (values: (string * obj) list) =
            constructor.GetParameters()
            |> Array.map (fun arg -> 
                values
                |> List.filter (fun (k, _) ->
                    k.Equals(arg.Name, StringComparison.InvariantCultureIgnoreCase))
                |> List.map (fun (_, v) -> v)
                |> List.tryHead
                |> Option.defaultWith (fun () -> sprintf "Could not find value for record type property %s" arg.Name |> JsonException |> raise))

        let buildRecordType (typ: Type) values =
            match typ.GetConstructors() with
            | [|constructor|] -> 
                getConstructorArgs constructor values
                |> constructor.Invoke
            | _ -> sprintf "Could not find constructor for record type %A" typ |> JsonException |> raise

        let getPropertyType (typ: Type) name =
            let props =
                typ.GetProperties()
                |> Seq.ofArray
                |> Seq.map (fun p -> (p.Name, p.PropertyType))

            let fields =
                typ.GetFields()
                |> Seq.ofArray
                |> Seq.map (fun p -> (p.Name, p.FieldType))

            Seq.concat [ props; fields ]
            |> Seq.filter (fun (n, _) -> n.Equals(name, StringComparison.InvariantCultureIgnoreCase))
            |> Seq.map (fun (_, v) -> v)
            |> Seq.tryHead

        let read (reader: byref<Utf8JsonReader>, typeToConvert: Type, options: JsonSerializerOptions) =
            
            let mutable brk = false
            let mutable values = []
            while not brk do
                match reader.Read() with
                | true when reader.TokenType = JsonTokenType.EndObject ->
                    brk <- true
                    ()
                | true ->
                    assertToken JsonTokenType.PropertyName reader.TokenType
                    let key = reader.GetString()
                    let objType = 
                        getPropertyType typeToConvert key
                        |> Option.defaultValue typedefof<obj>

                    let value = JsonSerializer.Deserialize(&reader, objType, options)
                    values <- List.concat [values; [(key, value)]]
                | false -> 
                    JsonException "Unexpected end of JSON sequence" |> raise

            buildRecordType typeToConvert values
    open Private1
    
    type RecordTypeConverter() =
        inherit JsonConverter<obj>()

        override __.CanConvert typeToConvert = FSharpType.IsRecord typeToConvert

        override __.Read(reader, typeToConvert, options) = read (&reader, typeToConvert, options)

        override __.Write(writer, value, options) = JsonSerializer.Serialize(writer, value, options)

module private Private =
    let asyncMap f x = async { 
        let! x1 = x; 
        return (f x1) 
    }
    
    let asyncBind f x = async { 
        let! x1 = x; 
        return! (f x1) 
    }

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

let serialize<'a> (dto: 'a) =
    let ms = new MemoryStream() :> Stream
    JsonSerializer.SerializeAsync(ms, dto, typedefof<'a>, null, Unchecked.defaultof<CancellationToken>)
    |> Async.AwaitTask
    |> asyncMap (fun _ -> 
        ms.Position <- 0L
        Disposables.buildFromDisposable ms [])
    |> asyncBind compress

let deserialize<'a> s =

    async {
        use! str = Disposables.build s [] |> decompress
        
        let s = Disposables.getValue str
        let options = JsonSerializerOptions()
        options.Converters.Add(Converters.RecordTypeConverter())
        let dtoT = JsonSerializer.DeserializeAsync<'a> (s, options)

        let! dto = dtoT.AsTask() |> Async.AwaitTask
        return dto
    }