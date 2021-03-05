namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.Shapes

module Helpers =
    open FParsec
    open Colour
    open Point
    open Shape
    open System.IO
    open Triangle
    open System.Collections.Generic

    //Exceptions
    exception VerticeException of string
    exception HeaderParserExpception of string
    exception TriangleException of string

    let calcNormal a b c = 
        let uVector = Point.distance a b
        let vVector = Point.distance a c
        let normal = Vector.normalise (Vector.crossProduct  vVector uVector)
        normal
    
    let updateMap (verticeId,triangleNormal) (vTMap : Dictionary<int,Vector.Vector>) =
        if vTMap.ContainsKey verticeId then 
            let list = vTMap.[verticeId] + triangleNormal
            vTMap.[verticeId] <- list
        else
            vTMap.Add (verticeId,triangleNormal)
        vTMap

    //Helpers
    let prepareList list = 
        let mapping (i:int) v = (i,v)
        List.mapi mapping list

    let prepareLists (vlist : Vertice list) (tlist : (int*int*int) list)  = 
        let vmap = Map.ofList(prepareList vlist)        
        let trimap = Map.ofList(prepareList tlist)
            
        (vmap,trimap)

    

    let calcNormals (verticeMap: Map<int,Vertice>) (verticeTriangleNormalMap:Dictionary<int,Vector.Vector>) = 
        let mutable nVerticeMap = Map.empty<int,Vertice>
        for KeyValue(k,v) in verticeMap do
            if(verticeTriangleNormalMap.ContainsKey k) then
                let normal = verticeTriangleNormalMap.Item k
                let verticeNormal = Vector.normalise normal
                let newVertice = mkVerticeTypes v.Point verticeNormal v.TextureCoordinate 
                nVerticeMap <- nVerticeMap.Add(k,newVertice)

        nVerticeMap

    
    let makeTriangles (verticeMap: Map<int,Vertice>) (triangles : Map<int,(int*int*int)>) (smooth:bool) = 
        let mutable newTriangles = List.Empty
        for KeyValue(_,(id1,id2,id3)) in triangles do
            let va = verticeMap.Item id1
            let vb = verticeMap.Item id2
            let vc = verticeMap.Item id3
            let triangle = mkPlyTriangle va vb vc smooth
            newTriangles <- triangle::newTriangles
        newTriangles

    let str = pstring
    let chr = pchar
    let ws = spaces
    
        