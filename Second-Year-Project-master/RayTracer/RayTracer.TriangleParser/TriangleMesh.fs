namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.Shapes.Shape
open RayTracer.Shapes.BaseShape
open RayTracer.TriangleParser.TriangleParser
open RayTracer.TriangleParser
open RayTracer.TriangleParser.Helpers
open RayTracer.KDTree.Construction
open RayTracer.KDTree.Traversal
open RayTracer.KDTree.KDTree
open Helpers
open Colour
open Material
open Point
open Ray
open BoundingBox
open Texture
open Hit

module TriangleMesh = 
    exception InvalidPlyFile    

    let triangleParse (v: Map<int,Vertice>) (triangles: Map<int,(int*int*int)>) = triangles |> Map.map (fun triId (id1,id2,id3) -> 
                                                                          let vertice1 = v.[id1]
                                                                          let vertice2 = v.[id2]
                                                                          let vertice3 = v.[id3]
                                                                          (vertice1,vertice2,vertice3))                                                      
    
    let newTrianglemesh (tree:KDTree) =  {   
        new BaseShape with 
            member this.Inside = None
            member this.BoundingBox = getBounds tree
            member this.TransformedBoundingBox = this.BoundingBox
            member this.Hit = fun ray tex -> traverse tree ray tex
        
    }


    let mkTriangleMesh path smooth = 
        match parsePly path with
            None -> raise InvalidPlyFile 
            | Some(vertices,triangles) ->
                
                let triangless = Helpers.makeTriangles vertices triangles smooth
                newTrianglemesh (mkTree (List.toArray triangless) )
