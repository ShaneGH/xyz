module ShinyHttpCache.Serialization.CachedValues.Serializer
open ShinyHttpCache.Serialization

module private Private =
    let asyncMap f x = async { 
        let! x1 = x; 
        return (f x1) 
    }
open Private

let serialize x = 
    CompressedSerialization.serialize<Dtos.CacheValuesDto> x
    |> asyncMap (fun x -> ((1us, 0us), x))