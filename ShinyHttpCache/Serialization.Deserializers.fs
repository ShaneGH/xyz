namespace ShinyHttpCache.Serialization.Deserailizers
open ShinyHttpCache.Serialization
open ShinyHttpCache.Utils

module V1 =
    let deserialize = CompressedSerialization.deserialize<Dtos.V1.CacheValuesDto>

