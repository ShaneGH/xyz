module ShinyHttpCache.Serialization.CachedValues.V1
open ShinyHttpCache.Serialization

let deserialize = CompressedSerialization.deserialize<Dtos.CacheValuesDto>