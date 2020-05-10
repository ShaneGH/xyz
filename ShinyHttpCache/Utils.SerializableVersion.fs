module ShinyHttpCache.Utils.SerializableVersion

open System
open System.Collections.Generic

let rec toSemanticVersion (vs: int seq) =
    match List.ofSeq vs with
    | [x1] -> toSemanticVersion [x1; 0]
    | [x1; x2;] -> Version (x1, x2)
    | [x1; x2; x3;] -> Version (x1, x2, x3)
    | [x1; x2; x3; x4;] -> Version (x1, x2, x3, x4)
    | xs ->
        xs
        |> List.fold (fun (s: string) v -> if s.Length = 0 then v.ToString() else sprintf "%s.%d" s v) ""
        |> sprintf "Invalid version: %s"
        |> invalidOp
    

let fromSemanticVersion (v: Version) =
    match v.Major, v.Minor, v.Build, v.Revision with
    | x1, x2, x3, _ when x2 < 1 && x3 < 0 -> [x1]
    | x1, x2, x3, _ when x3 < 0 -> [x1; x2]
    | x1, x2, x3, x4 when x4 < 0 -> [x1; x2; x3]
    | x1, x2, x3, x4 -> [x1; x2; x3; x4]
    |> List<int>