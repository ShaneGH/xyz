module ShinyHttpCache.Tests.CustomSerializerTests

open System
open System.Text.Json
open NUnit.Framework
open ShinyHttpCache.Utils

type Serializable<'a> () =
    let mutable value = Unchecked.defaultof<'a>
    member this.Value with get () = value
    member this.Value with set (v) = value <- v

module private Utils =
        
    let private options =
        let options = JsonSerializerOptions()
        options.Converters.Add(SerializationConverters.RecordType.Factory())
        options.Converters.Add(SerializationConverters.Option.Factory())
        options.Converters.Add(SerializationConverters.List.Factory())
        options.Converters.Add(SerializationConverters.Tuple.Factory())
        options.Converters.Add(SerializationConverters.Union.Factory())
        options

    let private optionsIgnoreNulls =
        let options = JsonSerializerOptions()
        options.IgnoreNullValues <- true
        options.Converters.Add(SerializationConverters.RecordType.Factory())
        options.Converters.Add(SerializationConverters.Option.Factory())
        options.Converters.Add(SerializationConverters.List.Factory())
        options.Converters.Add(SerializationConverters.Tuple.Factory())
        options.Converters.Add(SerializationConverters.Union.Factory())
        options
        
    let private executeAsProperty<'a> options value assertDeserialized =
            
        // arrange
        let expected = Serializable<'a>()
        expected.Value <- value

        // act
        let serialized = JsonSerializer.Serialize(expected, options)
        
        // assert
        match assertDeserialized with
        | Some (x: string) -> Assert.AreEqual(x, serialized, "serialized, as property")
        | None -> ()
        
        // act
        let deserialized = JsonSerializer.Deserialize<Serializable<'a>>(serialized, options)

        // assert
        Assert.AreEqual(value, deserialized.Value, "Deserialized, as property")
        
    let private executeAsRoot<'a> options value assertDeserialized =
            
        // arrange
        // act
        let serialized = JsonSerializer.Serialize<'a>(value, options)

        // assert
        match assertDeserialized with
        | Some (x: string) -> Assert.AreEqual(x, serialized, "Serialized, as root")
        | None -> ()
        
        // act
        let deserialized = JsonSerializer.Deserialize<'a>(serialized, options)
        
        // assert
        Assert.AreEqual(value, deserialized, "Deserialized, as root")
        
    module UseNulls =
        let executeAsProperty<'a> = executeAsProperty<'a> options
        let executeAsRoot<'a> = executeAsRoot<'a> options
        
    module IgnoreNulls =
        let executeAsProperty<'a> = executeAsProperty<'a> optionsIgnoreNulls
        let executeAsRoot<'a> = executeAsRoot<'a> optionsIgnoreNulls
open Utils

module Option =

    [<Test>]
    let ``Test option serializer, Some, reference type`` () =
        IgnoreNulls.executeAsProperty (Some "hello") (Some """{"Value":"hello"}""")
        UseNulls.executeAsProperty (Some "hello") (Some """{"Value":"hello"}""")

    [<Test>]
    let ``Test option serializer, None, reference type`` () =
        IgnoreNulls.executeAsProperty<string option> None (Some """{}""")
        UseNulls.executeAsProperty<string option> None (Some """{"Value":null}""")

    [<Test>]
    let ``Test option serializer, Some, value type`` () =
        IgnoreNulls.executeAsProperty (Some 77) (Some """{"Value":77}""")
        UseNulls.executeAsProperty (Some 77) (Some """{"Value":77}""")

    [<Test>]
    let ``Test option serializer, None, value type`` () =
        IgnoreNulls.executeAsProperty<int option> None (Some """{}""")
        UseNulls.executeAsProperty<int option> None (Some """{"Value":null}""")
    
module RecordType =
    type RecordType<'a> =
        {
            TheValue: 'a
        }
        
    [<Struct>]
    type StructRecord<'a> =
        {
            TheStructValue: 'a
        }

    [<Test>]
    let ``Test record type serializer, reference type property`` () =
        let rt = { TheValue = "the value" }
        
        IgnoreNulls.executeAsProperty rt (Some """{"Value":{"TheValue":"the value"}}""")
        UseNulls.executeAsProperty rt (Some """{"Value":{"TheValue":"the value"}}""")
        
        IgnoreNulls.executeAsRoot rt (Some """{"TheValue":"the value"}""")
        UseNulls.executeAsRoot rt (Some """{"TheValue":"the value"}""")

    [<Test>]
    let ``Test record type serializer, value type property`` () =
        let rt = { TheValue = 123 }
        
        IgnoreNulls.executeAsProperty rt (Some """{"Value":{"TheValue":123}}""")
        UseNulls.executeAsProperty rt (Some """{"Value":{"TheValue":123}}""")
        
        IgnoreNulls.executeAsRoot rt (Some """{"TheValue":123}""")
        UseNulls.executeAsRoot rt (Some """{"TheValue":123}""")

    [<Test>]
    let ``Test record type serializer, null reference type property`` () =
        let rt = { TheValue = null :> obj :?> System.String }
        
        IgnoreNulls.executeAsProperty rt (Some """{"Value":{}}""")
        UseNulls.executeAsProperty rt (Some """{"Value":{"TheValue":null}}""")
        
        IgnoreNulls.executeAsRoot rt (Some """{}""")
        UseNulls.executeAsRoot rt (Some """{"TheValue":null}""")

    [<Test>]
    let ``Test record type serializer, nullable value type property`` () =
        let rt = { TheValue = null :> obj :?> Nullable<int> }
        
        IgnoreNulls.executeAsProperty rt (Some """{"Value":{}}""")
        UseNulls.executeAsProperty rt (Some """{"Value":{"TheValue":null}}""")
        
        IgnoreNulls.executeAsRoot rt (Some """{}""")
        UseNulls.executeAsRoot rt (Some """{"TheValue":null}""")

    [<Test>]
    let ``Test record type serializer, struct record`` () =
        let rt = { TheStructValue = "the value" }
        
        IgnoreNulls.executeAsProperty rt (Some """{"Value":{"TheStructValue":"the value"}}""")
        UseNulls.executeAsProperty rt (Some """{"Value":{"TheStructValue":"the value"}}""")
        
        IgnoreNulls.executeAsRoot rt (Some """{"TheStructValue":"the value"}""")
        UseNulls.executeAsRoot rt (Some """{"TheStructValue":"the value"}""")
    
module List =

    [<Test>]
    let ``Test list serializer`` () =
        let rt = [1;4]
        
        IgnoreNulls.executeAsProperty rt (Some """{"Value":[1,4]}""")
        UseNulls.executeAsProperty rt (Some """{"Value":[1,4]}""")
        
        IgnoreNulls.executeAsRoot rt (Some """[1,4]""")
        UseNulls.executeAsRoot rt (Some """[1,4]""")
    
module Tuple =

    [<Test>]
    let ``Test tuple 1 serializer`` () =
        let rt = System.Tuple<int>(1)
        let str = """{"1":1}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 2 serializer`` () =
        let rt = (1, "hello")
        let str = """{"1":1,"2":"hello"}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 3 serializer`` () =
        let rt = (1, "hello", Some true)
        let str = """{"1":1,"2":"hello","3":true}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 4 serializer`` () =
        let rt = (1, "hello", Some true, 4)
        let str = """{"1":1,"2":"hello","3":true,"4":4}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 5 serializer`` () =
        let rt = (1, "hello", Some true, 4, "hello5")
        let str = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5"}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 6 serializer`` () =
        let rt = (1, "hello", Some true, 4, "hello5", Some false)
        let str = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5","6":false}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 7 serializer`` () =
        let rt = (1, "hello", Some true, 4, "hello5", Some false, 7)
        let str = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5","6":false,"7":7}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 8 serializer`` () =
        let rt = (1, "hello", Some true, 4, "hello5", Some false, 7, "hello8")
        let str = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5","6":false,"7":7,"rest":{"1":"hello8"}}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

    [<Test>]
    let ``Test tuple 9 serializer`` () =
        // 9 is 1 more than the max
        let rt = (1, "hello", Some true, 4, "hello5", Some false, 7, "hello8", None)
        let str = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5","6":false,"7":7,"rest":{"1":"hello8","2":null}}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)
        
        // TODO add support for nulls
//        let strNoNull = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5","6":false,"7":7,"8":{"1":"hello8"}}""";
//        IgnoreNulls.executeAsProperty rt (sprintf """{"Value":%s}""" strNoNull |> Some)
//        IgnoreNulls.executeAsRoot rt (sprintf """%s""" strNoNull |> Some)

        // this includes having None in the middle:
        // let rt = (1, None, true)
        
        // it also includes tuples of length 1, which have a different "write" method

    [<Test>]
    let ``Test tuple 10 serializer`` () =
        // 9 is 1 more than the max
        let rt = (1, "hello", Some true, 4, "hello5", Some false, 7, "hello8", None, "last")
        let str = """{"1":1,"2":"hello","3":true,"4":4,"5":"hello5","6":false,"7":7,"rest":{"1":"hello8","2":null,"3":"last"}}""";
        
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" str |> Some)
        UseNulls.executeAsRoot rt (sprintf """%s""" str |> Some)

module Union =
    
    type ElUnion =
        | T1
        | T2 of int
        | T3 of string
    
    type ElEnum =
        | E1 = 77
        | E2 = 78

    [<Test>]
    let ``Test union type serializer, no value`` () =
        let rt = T1
        let useNulls = """{"T1":[]}""";
        let ignoreNulls = """{"T1":[]}""";
        
        IgnoreNulls.executeAsProperty rt (sprintf """{"Value":%s}""" ignoreNulls |> Some)
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" useNulls |> Some)
        
        IgnoreNulls.executeAsRoot rt (ignoreNulls |> Some)
        UseNulls.executeAsRoot rt (useNulls |> Some)

    [<Test>]
    let ``Test union type serializer, reference type value`` () =
        let rt = T2 444
        let useNulls = """{"T2":[444]}""";
        let ignoreNulls = """{"T2":[444]}""";
        
        IgnoreNulls.executeAsProperty rt (sprintf """{"Value":%s}""" ignoreNulls |> Some)
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" useNulls |> Some)
        
        IgnoreNulls.executeAsRoot rt (ignoreNulls |> Some)
        UseNulls.executeAsRoot rt (useNulls |> Some)

    [<Test>]
    let ``Test union type serializer, value type value`` () =
        let rt = T3 "hi"
        let useNulls = """{"T3":["hi"]}""";
        let ignoreNulls = """{"T3":["hi"]}""";
        
        IgnoreNulls.executeAsProperty rt (sprintf """{"Value":%s}""" ignoreNulls |> Some)
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" useNulls |> Some)
        
        IgnoreNulls.executeAsRoot rt (ignoreNulls |> Some)
        UseNulls.executeAsRoot rt (useNulls |> Some)

    [<Test>]
    let ``Test union type serializer, null value type value`` () =
        let rt = null |> box :?> System.String |> T3
        let useNulls = """{"T3":[null]}""";
        let ignoreNulls = """{"T3":[null]}""";
        
        IgnoreNulls.executeAsProperty rt (sprintf """{"Value":%s}""" ignoreNulls |> Some)
        UseNulls.executeAsProperty rt (sprintf """{"Value":%s}""" useNulls |> Some)
        
        IgnoreNulls.executeAsRoot rt (ignoreNulls |> Some)
        UseNulls.executeAsRoot rt (useNulls |> Some)

    [<Test>]
    let ``Test union type serializer is not used for enums`` () =
        UseNulls.executeAsProperty ElEnum.E1 (sprintf """{"Value":77}""" |> Some)

//TODO: enums