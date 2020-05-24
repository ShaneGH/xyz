module ShinyHttpCache.Utils.SerializationConverters
open System.Collections.Concurrent
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Microsoft.FSharp.Reflection
open System
open System.Reflection
open System.Text.Json.Serialization

let private assertToken expected actual =
    match actual with
    | x when x = expected -> ()
    | _ -> sprintf "Invalid token type %A. Expecting %A." actual expected |> JsonException |> raise
    
module private ConverterCache =
    
    let private converterCache = ConcurrentDictionary<Type, JsonConverter>()
    let private canConvertCache = ConcurrentDictionary<(Type * Type), bool>()
    
    type Cache =
        {
            canConvert: Type -> bool
            getConverter: Type -> JsonConverter
        }
       
    let build factoryType buildCanConvert (buildConverter: Type -> JsonConverter) =
        
        let canConvertWithFullKey (_, x) = buildCanConvert x
        let canConvert x = canConvertCache.GetOrAdd((factoryType, x), canConvertWithFullKey)
            
        let getConverter x = converterCache.GetOrAdd(x, buildConverter)
        
        {
            canConvert = canConvert
            getConverter = getConverter
        }
            
module rec Option =

    module private ConverterInternal =
        let readRefType (reader: byref<Utf8JsonReader>) options =
            let result = JsonSerializer.Deserialize(&reader, typedefof<'a>, options) :?> 'a
            match result with
            | null -> None
            | x -> Some x

        let writeRefType writer value options =
            match value with
            | Some x -> JsonSerializer.Serialize(writer, x, options)
            | None -> JsonSerializer.Serialize(writer, null, options)
        
        let readValueType (reader: byref<Utf8JsonReader>) options =
            let result = JsonSerializer.Deserialize(&reader, typeof<Nullable<'a>>, options) :?> Nullable<'a>
            match result.HasValue with
            | false -> None
            | true -> Some result.Value

        let writeValueType<'a when 'a :> ValueType and 'a : struct and 'a : (new: Unit -> 'a)> writer value options =
            match value with
            | Some (x: 'a) -> JsonSerializer.Serialize(writer, x, options)
            | None -> JsonSerializer.Serialize(writer, Nullable<'a>(), options)

    type RefTypeConverter<'a when 'a : null>() =
        inherit JsonConverter<'a option>()
        override __.Read(reader, _, options) = ConverterInternal.readRefType &reader options
        override __.Write(writer, value, options) = ConverterInternal.writeRefType writer value options

    type private ValueTypeConverter<'a when 'a :> ValueType and 'a : struct and 'a : (new: Unit -> 'a)>() =
        inherit JsonConverter<'a option>()
        override __.Read(reader, _, options) = ConverterInternal.readValueType &reader options
        override __.Write(writer, value, options) = ConverterInternal.writeValueType writer value options
        
    let canConvert (typeToConvert: Type) =
        match typeToConvert.IsGenericType with
        | true -> typeToConvert.GetGenericTypeDefinition() = typedefof<Option<_>>
        | false -> false
            
    let createForType (typ: Type) =
        let optionType = typ.GetGenericArguments().[0]
        match optionType.IsValueType with
        | true ->
            typedefof<ValueTypeConverter<_>>.MakeGenericType optionType
            |> Activator.CreateInstance
            :?> JsonConverter
        | false ->
            typedefof<RefTypeConverter<_>>.MakeGenericType optionType
            |> Activator.CreateInstance
            :?> JsonConverter
            
    let private converterCache = ConverterCache.build typeof<Factory> canConvert createForType
        
    type Factory() =
        inherit JsonConverterFactory()
        override __.CanConvert x = converterCache.canConvert x
        override __.CreateConverter (typ, _) = converterCache.getConverter typ

module rec List =

    type Converter<'a>() =
        inherit JsonConverter<'a list>()
        override __.Read(reader, _, options) = JsonSerializer.Deserialize<'a seq>(&reader, options) |> List.ofSeq
        override __.Write(writer, value, options) = JsonSerializer.Serialize<'a seq>(writer, value, options)
        
    module private FactoryInternal =
        
        let canConvert (typeToConvert: Type) =
            match typeToConvert.IsGenericType with
            | true -> typeToConvert.GetGenericTypeDefinition() = typedefof<list<_>>
            | false -> false
                
        let createForType (typ: Type) =
            let collectionType = typ.GetGenericArguments().[0]
            
            typedefof<Converter<_>>.MakeGenericType collectionType
            |> Activator.CreateInstance
            :?> JsonConverter
                
    let private converterCache = ConverterCache.build typeof<Factory> FactoryInternal.canConvert FactoryInternal.createForType
        
    type Factory() =
        inherit JsonConverterFactory()
        override __.CanConvert x = converterCache.canConvert x
        override __.CreateConverter (typ, _) = converterCache.getConverter typ

module rec Tuple =
        
    module private ConverterInternal =
        let getSingle (name: string) (reader: byref<Utf8JsonReader>) options =
            match reader.Read(), assertToken JsonTokenType.PropertyName reader.TokenType, reader.ValueTextEquals name with
            | true, _, true ->
                JsonSerializer.Deserialize<'a>(&reader, options)
            | _ ->
                sprintf "Invalid token. Expecting \"%s\"." name |> JsonException |> raise
            
        let get<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h> depth (reader: byref<Utf8JsonReader>) options =
            
            let a =
                if depth > 0 then
                    getSingle "1" &reader options
                else
                    Unchecked.defaultof<'a>
            let b =
                if depth > 1 then
                    getSingle "2" &reader options
                else
                    Unchecked.defaultof<'b>
            let c =
                if depth > 2 then
                    getSingle "3" &reader options
                else
                    Unchecked.defaultof<'c>
            let d =
                if depth > 3 then
                    getSingle "4" &reader options
                else
                    Unchecked.defaultof<'d>
            let e =
                if depth > 4 then
                    getSingle "5" &reader options
                else
                    Unchecked.defaultof<'e>
            let f =
                if depth > 5 then
                    getSingle "6" &reader options
                else
                    Unchecked.defaultof<'f>
            let g =
                if depth > 6 then
                    getSingle "7" &reader options
                else
                    Unchecked.defaultof<'g>
            let h =
                if depth > 7 then
                    getSingle "rest" &reader options
                else
                    Unchecked.defaultof<'h>
                    
            match reader.Read(), assertToken JsonTokenType.EndObject reader.TokenType with
            | true, _ -> ()
            | false, _ -> printf "Unexpected end of json stream" |> JsonException |> raise
                
            (a, b, c, d, e, f, g, h)
        
        let writeSingle (name: string) value (writer: Utf8JsonWriter) options =
            writer.WritePropertyName(name)
            JsonSerializer.Serialize(writer, value, options)
            
        let write depth (writer: Utf8JsonWriter) value options =
            let (a, b, c, d, e, f, g, rest) = value
            writer.WriteStartObject()
            
            if depth > 0 then
                writeSingle "1" a writer options
            else ()
            if depth > 1 then
                writeSingle "2" b writer options
            else ()
            if depth > 2 then
                writeSingle "3" c writer options
            else ()
            if depth > 3 then
                writeSingle "4" d writer options
            else ()
            if depth > 4 then
                writeSingle "5" e writer options
            else ()
            if depth > 5 then
                writeSingle "6" f writer options
            else ()
            if depth > 6 then
                writeSingle "7" g writer options
            else ()
            if depth > 7 then
                writeSingle "rest" rest writer options
            else ()
            
            writer.WriteEndObject()
        
    type Converter<'a>() =
        inherit JsonConverter<Tuple<'a>>()
        let depth = 1
        
        override __.Read(reader, _, options) =
            let (a, _, _, _, _, _, _, _) = ConverterInternal.get<'a, Unit, Unit, Unit, Unit, Unit, Unit, Unit> depth &reader options
            System.Tuple<'a>(a)
        override __.Write(writer, value, options) =
            writer.WriteStartObject()
            ConverterInternal.writeSingle "1" value.Item1 writer options
            writer.WriteEndObject()
        
    type Converter<'a, 'b>() =
        inherit JsonConverter<('a * 'b)>()
        let depth = 2
        
        override __.Read(reader, _, options) =
            let (a, b, _, _, _, _, _, _) = ConverterInternal.get<'a, 'b, Unit, Unit, Unit, Unit, Unit, Unit> depth &reader options
            (a, b)
        override __.Write(writer, value, options) =
            let (a, b) = value
            let padded = (a, b, (), (), (), (), (), ())
            
            ConverterInternal.write depth writer padded options
        
    type Converter<'a, 'b, 'c>() =
        inherit JsonConverter<('a * 'b * 'c)>()
        let depth = 3
        
        override __.Read(reader, _, options) =
            let (a, b, c, _, _, _, _, _) = ConverterInternal.get<'a, 'b, 'c, Unit, Unit, Unit, Unit, Unit> depth &reader options
            (a, b, c)
        override __.Write(writer, value, options) =
            let (a, b, c) = value
            let padded = (a, b, c, (), (), (), (), ())
            
            ConverterInternal.write depth writer padded options
        
    type Converter<'a, 'b, 'c, 'd>() =
        inherit JsonConverter<('a * 'b * 'c * 'd)>()
        let depth = 4
        
        override __.Read(reader, _, options) =
            let (a, b, c, d, _, _, _, _) = ConverterInternal.get<'a, 'b, 'c, 'd, Unit, Unit, Unit, Unit> depth &reader options
            (a, b, c, d)
        override __.Write(writer, value, options) =
            let (a, b, c, d) = value
            let padded = (a, b, c, d, (), (), (), ())
            
            ConverterInternal.write depth writer padded options
        
    type Converter<'a, 'b, 'c, 'd, 'e>() =
        inherit JsonConverter<('a * 'b * 'c * 'd * 'e)>()
        let depth = 5
        
        override __.Read(reader, _, options) =
            let (a, b, c, d, e, _, _, _) = ConverterInternal.get<'a, 'b, 'c, 'd, 'e, Unit, Unit, Unit> depth &reader options
            (a, b, c, d, e)
        override __.Write(writer, value, options) =
            let (a, b, c, d, e) = value
            let padded = (a, b, c, d, e, (), (), ())
            
            ConverterInternal.write depth writer padded options
        
    type Converter<'a, 'b, 'c, 'd, 'e, 'f>() =
        inherit JsonConverter<('a * 'b * 'c * 'd * 'e * 'f)>()
        let depth = 6
        
        override __.Read(reader, _, options) =
            let (a, b, c, d, e, f, _, _) = ConverterInternal.get<'a, 'b, 'c, 'd, 'e, 'f, Unit, Unit> depth &reader options
            (a, b, c, d, e, f)
        override __.Write(writer, value, options) =
            let (a, b, c, d, e, f) = value
            let padded = (a, b, c, d, e, f, (), ())
            
            ConverterInternal.write depth writer padded options
        
    type Converter<'a, 'b, 'c, 'd, 'e, 'f, 'g>() =
        inherit JsonConverter<('a * 'b * 'c * 'd * 'e * 'f * 'g)>()
        let depth = 7
        
        override __.Read(reader, _, options) =
            let (a, b, c, d, e, f, g, _) = ConverterInternal.get<'a, 'b, 'c, 'd, 'e, 'f, 'g, Unit> depth &reader options
            (a, b, c, d, e, f, g)
        override __.Write(writer, value, options) =
            let (a, b, c, d, e, f, g) = value
            let padded = (a, b, c, d, e, f, g, ())
            
            ConverterInternal.write depth writer padded options
        
    type Converter<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h>() =
        inherit JsonConverter<System.Tuple<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h>>()
        let depth = 8
        
        override __.Read(reader, _, options) =
            let (a, b, c, d, e, f, g, h) = ConverterInternal.get<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h> depth &reader options
            System.Tuple<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h>(a, b, c, d, e, f, g, h)
        override __.Write(writer, value, options) =  
            let v = (
                        value.Item1,
                        value.Item2,
                        value.Item3,
                        value.Item4,
                        value.Item5,
                        value.Item6,
                        value.Item7,
                        value.Rest
                    )
            ConverterInternal.write depth writer v options
            
    module private FactoryInternal =
            
        let systemDotTuple = Regex(@"^System\.Tuple`[1-8](\[|$)", RegexOptions.Compiled)
            
        let canConvert (typeToConvert: Type) =
            // TODO: reflection
            match typeToConvert.IsGenericType with
            | true ->
                let typ = typeToConvert.GetGenericTypeDefinition()
                systemDotTuple.IsMatch typ.FullName
            | false -> false
                
        let createForType (typ: Type) =
            let genericTypeArguments = typ.GetGenericArguments()
            let converterType =
                match genericTypeArguments with
                | xs when xs.Length = 1 ->
                    fun xs -> typedefof<Converter<_>>.MakeGenericType xs
                | xs when xs.Length = 2 ->
                    fun xs -> typedefof<Converter<_, _>>.MakeGenericType xs
                | xs when xs.Length = 3 ->
                    fun xs -> typedefof<Converter<_, _, _>>.MakeGenericType xs
                | xs when xs.Length = 4 ->
                    fun xs -> typedefof<Converter<_, _, _, _>>.MakeGenericType xs
                | xs when xs.Length = 5 ->
                    fun xs -> typedefof<Converter<_, _, _, _, _>>.MakeGenericType xs
                | xs when xs.Length = 6 ->
                    fun xs -> typedefof<Converter<_, _, _, _, _, _>>.MakeGenericType xs
                | xs when xs.Length = 7 ->
                    fun xs -> typedefof<Converter<_, _, _, _, _, _, _>>.MakeGenericType xs
                | xs when xs.Length = 8 ->
                    fun xs -> typedefof<Converter<_, _, _, _, _, _, _, _>>.MakeGenericType xs
                | xs ->
                    sprintf "Unable to create tuple serializer for tuple of length %i" xs.Length
                    |> JsonException
                    |> raise
            
            genericTypeArguments
            |> converterType
            |> Activator.CreateInstance
            :?> JsonConverter
            
    let private converterCache = ConverterCache.build typeof<Factory> FactoryInternal.canConvert FactoryInternal.createForType
        
    type Factory() =
        inherit JsonConverterFactory()
        override __.CanConvert x = converterCache.canConvert x
        override __.CreateConverter (typ, _) = converterCache.getConverter typ
            
module rec Union =
    
    module private ConverterInternal =
        let getCase (cases: UnionCaseInfo list) name =
            cases
            |> List.filter (fun x -> x.Name = name)
            |> List.tryExactlyOne
            |> Option.defaultWith (fun () ->
                sprintf "Could not find case for %s" name
                |> JsonException
                |> raise)
        
        let jsonReader (cases: UnionCaseInfo list) (reader: byref<Utf8JsonReader>) options =
            
            match reader.Read(), assertToken JsonTokenType.PropertyName reader.TokenType with
            | true, _ ->
                let case =
                    Encoding.UTF8.GetString reader.ValueSpan
                    |> getCase cases
                    
                match reader.Read(), assertToken JsonTokenType.StartArray reader.TokenType with
                | true, _ ->
                    // Ref structs cannot be used in closures, so immutable code
                    // is difficult to do here
                    let mutable i = 0
                    let fields = case.GetFields()
                    let objs = Array.create<obj> fields.Length null
                    while i < fields.Length do
                        match reader.Read() with
                        | true ->
                            objs.[i] <- JsonSerializer.Deserialize (&reader, fields.[i].PropertyType, options)
                            i <- i + 1
                        | false -> 
                            "Unexpected end of document"
                            |> JsonException
                            |> raise
                            
                    reader.Read() |> ignore
                    assertToken JsonTokenType.EndArray reader.TokenType
                            
                    reader.Read() |> ignore
                    assertToken JsonTokenType.EndObject reader.TokenType
                    
                    FSharpValue.MakeUnion(case, objs)
                    |> Some
                | false, _ -> None
            | false, _ -> None
            |> Option.defaultWith (fun () ->
                "Unexpected end of document"
                |> JsonException
                |> raise)
            
        let jsonWriter (w: Utf8JsonWriter) (v: obj) (o: JsonSerializerOptions): Unit =
            let (case, data) = FSharpValue.GetUnionFields (v, v.GetType())
            w.WriteStartObject()
            w.WritePropertyName(case.Name)
            JsonSerializer.Serialize (w, data, o) 
            w.WriteEndObject()

    type Converter<'a>() =
        inherit JsonConverter<'a>()
        
        let cases = typeof<'a> |> FSharpType.GetUnionCases |> List.ofArray
        
        override __.Read(reader, _, options) = ConverterInternal.jsonReader cases &reader options :?> 'a
        override __.Write(writer, value, options) = ConverterInternal.jsonWriter writer value options
        
    module private FactoryInternal =
        let canConvert (typeToConvert: Type) =
            // TODO: reflection
            match Option.canConvert typeToConvert with
            | true -> false    // there is a better converter for options with null support
            | false -> FSharpType.IsUnion typeToConvert
                
        let createForType (typ: Type) =
            typedefof<Converter<_>>.MakeGenericType typ
            |> Activator.CreateInstance
            :?> JsonConverter
            
    let private converterCache = ConverterCache.build typeof<Factory> FactoryInternal.canConvert FactoryInternal.createForType
        
    type Factory() =
        inherit JsonConverterFactory()
        override __.CanConvert x = converterCache.canConvert x
        override __.CreateConverter (typ, _) = converterCache.getConverter typ

module rec RecordType =
    
    module private ConverterInternal =
        
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
        
    type Converter<'a>() =
        inherit JsonConverter<'a>()

        override __.Read(reader, typeToConvert, options) = ConverterInternal.read (&reader, typeToConvert, options) :?> 'a

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
                
    module private FactoryInternal =
        
        let canConvert (typeToConvert: Type) =
            // TODO: reflection
            match typeToConvert.IsGenericType with
            | true -> typeToConvert.GetGenericTypeDefinition() = typedefof<list<_>>
            | false -> false
                
        let createForType (typ: Type) =
            let converterType = typedefof<Converter<_>>.MakeGenericType typ
            Activator.CreateInstance converterType :?> JsonConverter
            
    let private converterCache = ConverterCache.build typeof<Factory> FactoryInternal.canConvert FactoryInternal.createForType
        
    type Factory() =
        inherit JsonConverterFactory()

        override __.CanConvert typeToConvert = converterCache.canConvert typeToConvert
        
        override __.CreateConverter (typ, options) = converterCache.getConverter typ