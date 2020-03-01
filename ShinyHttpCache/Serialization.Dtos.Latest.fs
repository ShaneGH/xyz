module ShinyHttpCache.Serialization.Dtos.Latest

open ShinyHttpCache.Serialization.Dtos

type CacheValuesDto = V1.CacheValuesDto
let toDto = V1.toDto
let fromDto = V1.fromDto