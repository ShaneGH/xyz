module ShinyHttpCache.Serialization.Serializer

open ShinyHttpCace.Utils
open ShinyHttpCache.Serialization
open ShinyHttpCache.Utils

let serialize = 
    Dtos.Latest.toDto
    >> Infra.Async.bind CompressedSerialization.serialize<Dtos.Latest.CacheValuesDto>
    >> Infra.Async.map (fun x -> ((1us, 0us), x))