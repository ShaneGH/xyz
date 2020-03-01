module ShinyHttpCache.Serialization.Serializer

open ShinyHttpCache.Serialization
open ShinyHttpCache.Utils

module private Private =
    let asyncMap f x = async { 
        let! x1 = x; 
        return (f x1) 
    }
    
    let asyncBind f x = async { 
        let! x1 = x; 
        return! (f x1) 
    }
open Private

let serialize x = 
    Dtos.Latest.toDto x
    |> asyncBind CompressedSerialization.serialize<Dtos.Latest.CacheValuesDto>
    |> asyncMap (fun x -> ((1us, 0us), x))