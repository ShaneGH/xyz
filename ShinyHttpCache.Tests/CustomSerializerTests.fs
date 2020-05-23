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

    let private optionsIgnoreNulls =
        let options = JsonSerializerOptions()
        options.IgnoreNullValues <- true
        options.Converters.Add(SerializationConverters.RecordTypes.Factory())
        options.Converters.Add(SerializationConverters.Options.Factory())
        options
        
    let private options =
        let options = JsonSerializerOptions()
        options.Converters.Add(SerializationConverters.RecordTypes.Factory())
        options.Converters.Add(SerializationConverters.Options.Factory())
        options
        
    let private executeAsProperty<'a> options value assertDeserialized =
            
        // arrange
        let expected = Serializable<'a>()
        expected.Value <- value

        // act
        let serialized = JsonSerializer.Serialize(expected, options)
        let deserialized = JsonSerializer.Deserialize<Serializable<'a>>(serialized, options)

        // assert
        match assertDeserialized with
        | Some (x: string) -> Assert.AreEqual(x, serialized, "serialized, as property")
        | None -> ()
        
        Assert.AreEqual(value, deserialized.Value, "Deserialized, as property")
        
    let private executeAsRoot<'a> options value assertDeserialized =
            
        // arrange
        // act
        let serialized = JsonSerializer.Serialize<'a>(value, options)
        let deserialized = JsonSerializer.Deserialize<'a>(serialized, options)

        // assert
        match assertDeserialized with
        | Some (x: string) -> Assert.AreEqual(x, serialized, "Serialized, as root")
        | None -> ()
        
        Assert.AreEqual(value, deserialized, "Deserialized, as root")
        
    module UseNulls =
        let executeAsProperty<'a> = executeAsProperty<'a> options
        let executeAsRoot<'a> = executeAsRoot<'a> options
        
    module IgnoreNulls =
        let executeAsProperty<'a> = executeAsProperty<'a> optionsIgnoreNulls
        let executeAsRoot<'a> = executeAsRoot<'a> optionsIgnoreNulls
open Utils

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
    
type RecordType<'a> =
    {
        TheValue: 'a
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
let ``Test value type serializer, null reference type property`` () =
    let rt = { TheValue = null :> obj :?> Nullable<int> }
    
    IgnoreNulls.executeAsProperty rt (Some """{"Value":{}}""")
    UseNulls.executeAsProperty rt (Some """{"Value":{"TheValue":null}}""")
    
    IgnoreNulls.executeAsRoot rt (Some """{}""")
    UseNulls.executeAsRoot rt (Some """{"TheValue":null}""")