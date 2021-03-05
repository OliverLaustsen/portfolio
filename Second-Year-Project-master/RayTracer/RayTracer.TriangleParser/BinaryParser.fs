namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.TriangleParser.HeaderParser
open RayTracer.TriangleParser.Helpers
open FParsec
open Colour
open RayTracer.Shapes.Shape
open Point
open System.IO
open System 
open System.Collections.Generic

module BinaryParser =
    let getCorrectBytes (format : Format) (bytes : byte []) = 
        let isLittleEndian = BitConverter.IsLittleEndian

        if(isLittleEndian && format = LITEND) then bytes
        else if((not isLittleEndian) && format = BIGEND) then bytes
        else Array.Reverse(bytes) 
             bytes

    let getFloatFromBinary (stream:Stream) format = 
        let buffer : byte [] = Array.zeroCreate(4)
        stream.Read (buffer,0,4) |> ignore
        BitConverter.ToSingle (getCorrectBytes format buffer, 0) 

    let getIntFromBinary (stream:Stream) format = 
        let buffer : byte [] = Array.zeroCreate(4)
        stream.Read (buffer,0,4) |> ignore
        BitConverter.ToInt32 (getCorrectBytes format buffer, 0) 
    
    let skipBytes (stream:Stream) count =
        let buffer : byte [] = Array.zeroCreate(4*count)
        stream.Read (buffer,0,4*count) |> ignore

    let skip (stream:Stream) count =
        let buffer : byte [] = Array.zeroCreate(count)
        stream.Read (buffer,0,count) |> ignore

    let getVertice3FromBinary (stream:Stream) format = 
        let x = float (getFloatFromBinary stream format)
        let y = float (getFloatFromBinary stream format)
        let z = float (getFloatFromBinary stream format)
        mkVertice3 x y z

    let getVertice5FromBinary (stream:Stream) format = 
        let x = float (getFloatFromBinary stream format)
        let y = float (getFloatFromBinary stream format)
        let z = float (getFloatFromBinary stream format)
        let u = float (getFloatFromBinary stream format)
        let v = float (getFloatFromBinary stream format)
        mkVertice5 x y z u v

    let getVertice6FromBinary (stream:Stream) format = 
        let x = float (getFloatFromBinary stream format)
        let y = float (getFloatFromBinary stream format)
        let z = float (getFloatFromBinary stream format)
        let nx = float (getFloatFromBinary stream format)
        let ny = float (getFloatFromBinary stream format)
        let nz = float (getFloatFromBinary stream format)
        mkVertice6 x y z nx ny nz

    let getVertice8FromBinary (stream:Stream) format = 
        let x = float (getFloatFromBinary stream format)
        let y = float (getFloatFromBinary stream format)
        let z = float (getFloatFromBinary stream format)
        let nx = float (getFloatFromBinary stream format)
        let ny = float (getFloatFromBinary stream format)
        let nz = float (getFloatFromBinary stream format)
        let u = float (getFloatFromBinary stream format)
        let v = float (getFloatFromBinary stream format)
        mkVertice x y z nx ny nz u v
    
    let pBinaryVertices (verticeParser:(Stream -> Format -> Vertice)) (skBefore,skAfter) count stream format = 
        let mutable vertices = Map.empty 
        for i in [0..(count-1)]  do
            if(skBefore > 0) then skip stream skBefore 
            let v = verticeParser stream format 
            if(skAfter > 0) then skip stream skAfter 
            vertices <- vertices.Add(i,v)
        vertices
    
    let pBinaryTriangle stream format = 
        let v1 = getIntFromBinary stream format
        let v2 = getIntFromBinary stream format
        let v3 = getIntFromBinary stream format
        (v1,v2,v3)
        
        

    let pBinaryTriangles count (skbefore,skafter) stream format (verticeMap : Map<int,Vertice>)  =
        let mutable triangles = Map.empty
        let mutable vtMap = new Dictionary<int,Vector.Vector>();
        for i in [0..(count-1)] do 
            if(skbefore > 0) then skip stream skbefore 
            let t = pBinaryTriangle stream format
            if(skafter > 0) then skip stream skafter 
            triangles <- triangles.Add(i,t)
            let (v1,v2,v3) = t
            let va = verticeMap.Item v1
            let vb = verticeMap.Item v2
            let vc = verticeMap.Item v3
                  
            let normal = (calcNormal va.Point vb.Point vc.Point)
            let nmap = vtMap |> updateMap(v1,normal) |> updateMap(v2,normal) |> updateMap(v3,normal)      
            vtMap <- nmap
        (triangles,vtMap)


    let pBinaryTrianglesN count (skbefore,skafter) stream format (verticeMap : Map<int,Vertice>)  =
        let mutable triangles = Map.empty
        let mutable vtMap = Map.empty
        for i in [0..(count-1)] do 
            if(skbefore > 0) then skip stream skbefore 
            let t = pBinaryTriangle stream format
            if(skafter > 0) then skip stream skafter 
            triangles <- triangles.Add(i,t)        
        (triangles)
        

    let getVerticeParser header = 
        let validPly = header |> HeaderParser.validPly
        let hasUV = header |> HeaderParser.hasUV
        let hasN = header |> HeaderParser.hasN
        
        if(validPly && hasN && hasUV) then getVertice8FromBinary
        else if(validPly && hasN) then  getVertice6FromBinary
        else if(validPly && hasUV) then getVertice5FromBinary
        else if(validPly) then getVertice3FromBinary
        else getVertice3FromBinary

    let parseBinary (startpos,header) (path:string) =
        use stream = new FileStream(path,FileMode.Open)
        let mutable vertices = List.Empty
        
        //Skip all the chars from the header
        skip stream startpos

        let vCount = header |> HeaderParser.getVerticeCount
        let tCount = header |> HeaderParser.getTriangleCount
        let format = header |> HeaderParser.getFormat

        let vAtts = getVerticeAtt header
        let tAtts = getTriangleAtt header
        let vSkippable = getSkippableVerticeBytes vAtts
        let tSkippable = getSkippableTriangleBytes tAtts

        let hasN = header |> HeaderParser.hasN  
        let vparser = getVerticeParser header
        
        if(hasN) then 
            let vertices = pBinaryVertices vparser vSkippable vCount stream format
            let triangles = pBinaryTrianglesN tCount tSkippable stream format vertices 
            Some(vertices,triangles)
        else
            let vertices = pBinaryVertices vparser vSkippable vCount stream format
            let (triangles,verticeTrianglemap) = pBinaryTriangles tCount tSkippable stream format vertices 
            let vs = calcNormals vertices verticeTrianglemap
            Some(vs,triangles)