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
open ShinyHttpCache.Utils.Disposables

module Converters =

    module Private1 =
        let assertToken expected actual =
            match actual with
            | x when x = expected -> ()
            | _ -> sprintf "Invalid token type %A. Expecting %A." actual expected |> JsonException |> raise
            
        let isNullable (t: Type) = t.IsConstructedGenericType && t.GetGenericTypeDefinition() = typedefof<Nullable<_>>

        let getConstructorArgs (constructor: ConstructorInfo) (values: (string * obj) list) implicitNulls =
            constructor.GetParameters()
            |> Array.map (fun arg -> 
                values
                |> List.filter (fun (k, _) ->
                    k.Equals(arg.Name, StringComparison.InvariantCultureIgnoreCase))
                |> List.map (fun (_, v) -> v)
                |> List.tryHead
                |> Option.defaultWith (fun () ->
                    match (implicitNulls, arg.ParameterType.IsValueType) with
                    | true, false -> null
                    // TODO: reflection
                    | true, _ when isNullable arg.ParameterType -> null
                    | _ ->
                        sprintf "Could not find value for record type property \"%s\"" arg.Name |> JsonException |> raise))

        let buildRecordType (typ: Type) values implicitNulls =
            // TODO: reflection
            match typ.GetConstructors() with
            | [|constructor|] -> 
                getConstructorArgs constructor values implicitNulls
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

            buildRecordType typeToConvert values options.IgnoreNullValues
    open Private1
    
    type RecordTypeConverter() =
        inherit JsonConverter<obj>()

        override __.CanConvert typeToConvert = FSharpType.IsRecord typeToConvert

        override __.Read(reader, typeToConvert, options) = read (&reader, typeToConvert, options)

        override __.Write(writer, value, options) =
            // todo: reflection
            match value with
            | null -> ()
            | x ->
                writer.WriteStartObject()
                x.GetType().GetFields()
                |> Array.map (fun f ->
                    match f.GetValue x, options.IgnoreNullValues with
                    | null, true -> ()
                    | value, _ ->
                        writer.WritePropertyName f.Name
                        JsonSerializer.Serialize(writer, value, options)
                )
                |> ignore
                
                x.GetType().GetProperties()
                |> Array.map (fun p ->
                    match p.GetValue x, options.IgnoreNullValues with
                    | null, true -> ()
                    | value, _ ->
                        writer.WritePropertyName p.Name
                        JsonSerializer.Serialize(writer, value, options)
                )
                |> ignore
                
                writer.WriteEndObject()
    
    type ReferenceTypeOptionConverter<'a when 'a : null>() =
        inherit JsonConverter<'a option>()
        
        override __.Read(reader, typeToConvert, options) =
            let result = JsonSerializer.Deserialize(&reader, typedefof<'a>, options) :?> 'a
            match result with
            | null -> None
            | x -> Some x

        override __.Write(writer, value, options) =
            match value with
            | Some x -> JsonSerializer.Serialize(writer, x, options)
            | None -> JsonSerializer.Serialize(writer, Unchecked.defaultof<'a>, options)
        
    type ValueTypeOptionConverter<'a when 'a :> ValueType and 'a : struct and 'a : (new: Unit -> 'a)>() =
        inherit JsonConverter<'a option>()
        
        override __.Read(reader, typeToConvert, options) =
            let result = JsonSerializer.Deserialize(&reader, typedefof<Nullable<'a>>, options) :?> Nullable<'a>
            match result.HasValue with
            | false -> None
            | true -> Some result.Value

        override __.Write(writer, value, options) =
            match value with
            | Some x -> JsonSerializer.Serialize(writer, x, options)
            | None -> JsonSerializer.Serialize(writer, Unchecked.defaultof<Nullable<'a>>, options)
        
    type OptionConvertorFactory() =
        inherit JsonConverterFactory()
        
        override __.CanConvert typeToConvert =
            // TODO: reflection
            match typeToConvert.IsGenericType with
            | true -> typeToConvert.GetGenericTypeDefinition() = typedefof<Option<_>>
            | false -> false
        
        override __.CreateConverter (typ, options) =
            let optionType = typ.GetGenericArguments().[0]
            
            // TODO: reflection
            match optionType.IsValueType with
            | true ->
                typedefof<ValueTypeOptionConverter<_>>.MakeGenericType optionType
                |> Activator.CreateInstance
                :?> JsonConverter
            | false ->
                typedefof<ReferenceTypeOptionConverter<_>>.MakeGenericType optionType
                |> Activator.CreateInstance
                :?> JsonConverter

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

let private buildSerializationOptions isDeserialize =
    // TODO: there is caching that can be done here
    let options = JsonSerializerOptions()
    options.IgnoreNullValues <- true
    
//    match isDeserialize with
//    | true -> options.Converters.Add(Converters.RecordTypeConverter())
    options.Converters.Add(Converters.RecordTypeConverter())
    //| false -> ()
    
    options.Converters.Add(Converters.OptionConvertorFactory())
    options

let serialize<'a> (dto: 'a) =
    let ms = new MemoryStream() :> Stream
    JsonSerializer.SerializeAsync(ms, dto, typedefof<'a>, buildSerializationOptions false, Unchecked.defaultof<CancellationToken>)
    |> Async.AwaitTask
    |> asyncMap (fun _ ->
        ms.Position <- 0L
        Disposables.buildFromDisposable ms [])
    |> asyncBind compress

let deserialize<'a> s =

    async {
        use! str = Disposables.build s [] |> decompress
        
        let s = Disposables.getValue str
        let dtoT = JsonSerializer.DeserializeAsync<'a> (s, buildSerializationOptions true)

        let! dto = dtoT.AsTask() |> Async.AwaitTask
        return dto
    }