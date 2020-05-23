module ShinyHttpCache.Utils.SerializationConverters
open System.Collections.Concurrent
open System.Text.Json
open Microsoft.FSharp.Reflection
open System
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization

module private ConverterCache =
    
    let private cache = ConcurrentDictionary<Type, JsonConverter>()
    
    let get typ =
        let (ok, result) = cache.TryGetValue typ
        match ok with | true -> Some result | false -> None
    
    let put typ value =
        cache.TryAdd (typ, value) |> ignore

module RecordTypes =
    let private assertToken expected actual =
        match actual with
        | x when x = expected -> ()
        | _ -> sprintf "Invalid token type %A. Expecting %A." actual expected |> JsonException |> raise
        
    let private isNullable (t: Type) = t.IsConstructedGenericType && t.GetGenericTypeDefinition() = typedefof<Nullable<_>>

    let private getConstructorArgs (constructor: ConstructorInfo) (values: (string * obj) list) implicitNulls =
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

    let private buildRecordType (typ: Type) values implicitNulls =
        // TODO: reflection
        match typ.GetConstructors() with
        | [|constructor|] -> 
            getConstructorArgs constructor values implicitNulls
            |> constructor.Invoke
        | _ -> sprintf "Could not find constructor for record type %A" typ |> JsonException |> raise

    let private getPropertyType (typ: Type) name =
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

    let private read (reader: byref<Utf8JsonReader>, typeToConvert: Type, options: JsonSerializerOptions) =
        
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
        
    type private Converter<'a>() =
        inherit JsonConverter<'a>()

        override __.Read(reader, typeToConvert, options) = read (&reader, typeToConvert, options) :?> 'a

        override __.Write(writer, value, options) =
            // todo: reflection
            match value with
            | x when box x = null -> ()
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
    
    type Factory() =
        inherit JsonConverterFactory()

        override __.CanConvert typeToConvert = FSharpType.IsRecord typeToConvert
        
        override __.CreateConverter (typ, options) =
            // todo: cache
            let converterType = typedefof<Converter<_>>.MakeGenericType typ
            Activator.CreateInstance converterType :?> JsonConverter
            
module Options =

    type private ReferenceTypeConverter<'a when 'a : null>() =
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
            
    module ValueTypeUtils =
        
        let read (reader: byref<Utf8JsonReader>) options =
            let result = JsonSerializer.Deserialize(&reader, typeof<Nullable<'a>>, options) :?> Nullable<'a>
            match result.HasValue with
            | false -> None
            | true -> Some result.Value

        let write<'a when 'a :> ValueType and 'a : struct and 'a : (new: Unit -> 'a)> writer value options =
            match value with
            | Some (x: 'a) -> JsonSerializer.Serialize(writer, x, options)
            | None -> JsonSerializer.Serialize(writer, Nullable<'a>(), options)

    type private ValueTypeConverter<'a when 'a :> ValueType and 'a : struct and 'a : (new: Unit -> 'a)>() =
        inherit JsonConverter<'a option>()
        override __.Read(reader, _, options) = ValueTypeUtils.read &reader options
        override __.Write(writer, value, options) = ValueTypeUtils.write writer value options
        
    let canConvert (typeToConvert: Type) =
        // TODO: reflection
        match typeToConvert.IsGenericType with
        | true -> typeToConvert.GetGenericTypeDefinition() = typedefof<Option<_>>
        | false -> false
            
    let createForType (typ: Type) =
        let optionType = typ.GetGenericArguments().[0]
        
        // TODO: reflection
        match optionType.IsValueType with
        | true ->
            typedefof<ValueTypeConverter<_>>.MakeGenericType optionType
            |> Activator.CreateInstance
            :?> JsonConverter
        | false ->
            typedefof<ReferenceTypeConverter<_>>.MakeGenericType optionType
            |> Activator.CreateInstance
            :?> JsonConverter
            
    let getForType typ =
        match ConverterCache.get typ with
        | Some x -> x
        | None ->
            let result = createForType typ
            ConverterCache.put typ result
            result
        
    type Factory() =
        inherit JsonConverterFactory()
        override __.CanConvert x = canConvert x
        override __.CreateConverter (typ, _) = getForType typ