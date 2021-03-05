namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.TriangleParser.Helpers
open RayTracer.TriangleParser.HeaderParser
open FParsec
open Colour
open RayTracer.Shapes.Shape
open Point
open System.IO
open System 
open System.Collections.Generic

module TxtParser =
   

    //Parsing of triangle vertices
    let pCoordinate = pfloat .>> (str " " <|> str "")
    let pCoordinates = tuple3 pCoordinate pCoordinate pCoordinate
    let p2DCoordinates = tuple2 pCoordinate pCoordinate
    let p3 = pCoordinates .>> ws
    let p5 = tuple2 pCoordinates p2DCoordinates
    let p6 = tuple2 pCoordinates pCoordinates
    let p8 = tuple3 pCoordinates pCoordinates p2DCoordinates
    let pVertice = p3

    let pVertice3 : Parser<Vertice,_> =
        fun stream ->      
            let res = run p3 (stream.ReadRestOfLine(true))
            match res with
                | Success((x,y,z),_,_) -> Reply (mkVertice3 x y z)
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)

    let pVertice5 : Parser<Vertice,_> =
        fun stream ->      
            let res = run p5 (stream.ReadRestOfLine(true))
            match res with
                | Success(((x,y,z),(u,v)),_,_) -> Reply (mkVertice5 x y z u v)
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)
    
    let pVertice6 : Parser<Vertice,_> = 
        fun stream ->      
            let res = run p6 (stream.ReadRestOfLine(true))
            match res with
                | Success(((x,y,z),(nx,ny,nz)),_,_) -> Reply (mkVertice6 x y z nx ny nz)
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)
    
    let pVertice8 : Parser<Vertice,_> =
        fun stream ->      
            let res = run p8 (stream.ReadRestOfLine(true))
            match res with
                | Success(((x,y,z),(nx,ny,nz),(u,v)),_,_) -> Reply (mkVertice x y z nx ny nz u v)
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)

    
    //Parsing of triangles
    let pTriVertice = pint32 .>> (str " " <|> str "")
    let pTriangle = ws >>. str "3 " >>. tuple3 pTriVertice pTriVertice pTriVertice .>> ((newline >>% "\\n") <|> (eof >>% ""))
    
    let rec pVertices (verticeParser : Parser<Vertice,unit>) (vertices:Map<int,Vertice>) count counter (lines:string list) = 
        if(counter < count) then 
            match lines with 
                line::rest ->  
                    match run verticeParser line with 
                        | Success(v,_,_) -> pVertices verticeParser (vertices.Add(counter,v)) count (counter+1) rest
                        | Failure(errorMsg,_,_) -> raise (VerticeException errorMsg)                
               | _ -> (vertices,lines)
        else 
            (vertices,lines)

    let rec pTriangles count counter (vertices:Map<int,Vertice>) (vtMap : Dictionary<int,Vector.Vector>) (triangles:Map<int,int*int*int>) (lines:string list) = 
        if(counter < count) then 
            match lines with 
                line::rest ->  
                    match run pTriangle line with 
                        | Success(t,_,_) -> 
                            let ntriangles = triangles.Add(counter,t)
                            let (v1,v2,v3) = t
                            let va = vertices.Item v1
                            let vb = vertices.Item v2
                            let vc = vertices.Item v3
                 
                            let normal = (calcNormal va.Point vb.Point vc.Point)
                            let nmap = vtMap |> updateMap(v1,normal) |> updateMap(v2,normal) |> updateMap(v3,normal)

                            pTriangles count (counter+1) vertices nmap ntriangles rest
                        | Failure(errorMsg,_,_) -> raise (TriangleException errorMsg)                 
               | _ -> (triangles,vtMap)
        else 
            (triangles,vtMap)
     
    let rec pTrianglesN count counter (triangles:Map<int,int*int*int>) (lines:string list) = 
        if(counter < count) then 
            match lines with 
                line::rest ->  
                    match run pTriangle line with 
                        | Success(t,_,_) -> 
                            let ntriangles = triangles.Add(counter,t)
                            pTrianglesN count (counter+1) ntriangles rest
                        | Failure(errorMsg,_,_) -> raise (TriangleException errorMsg)                 
               | _ -> (triangles)
        else 
            (triangles)

    
    let getVerticeParser header = 
        let validPly = header |> HeaderParser.validPly
        let hasUV = header |> HeaderParser.hasUV
        let hasN = header |> HeaderParser.hasN
        
        if(validPly && hasN && hasUV) then  pVertice8
        else if(validPly && hasN) then  pVertice6
        else if(validPly && hasUV) then pVertice5
        else if(validPly) then pVertice3
        else pVertice8

    let rec skipHeader (lines:string list) =
        match lines with
            [] -> []
            | line::rest -> if(line = "end_header") then rest
                            else skipHeader rest 

    let parseTxt header (path:string) =
        use reader = new StreamReader(path)
        let lines = Array.toList(File.ReadAllLines path)

        let actual = lines |> skipHeader

        let verticeParser = getVerticeParser header
        let hasN = header |> HeaderParser.hasN     
        let verticeCount = header |> HeaderParser.getVerticeCount
        let triangleCount = header |> HeaderParser.getTriangleCount

        if(hasN) then 
            let (vertices,restOfLines) = pVertices verticeParser Map.empty verticeCount 0 actual
            let triangles = pTrianglesN triangleCount 0 Map.empty restOfLines
            Some(vertices,triangles)
        else
            let (vertices,restOfLines) = pVertices verticeParser Map.empty verticeCount 0 actual
            let (triangles,verticeTrianglemap) = pTriangles triangleCount 0 vertices (new Dictionary<int,Vector.Vector>()) Map.empty restOfLines
            let stopWatch3 = System.Diagnostics.Stopwatch.StartNew()
            let vs = calcNormals vertices verticeTrianglemap
            Some(vs,triangles)
  