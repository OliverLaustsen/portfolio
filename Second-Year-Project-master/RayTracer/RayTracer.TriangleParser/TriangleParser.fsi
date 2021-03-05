namespace RayTracer.TriangleParser
open FParsec
open RayTracer.Entities
open RayTracer.Shapes.Shape

module TriangleParser =  
    val parsePly : file:string -> (Map<int,Vertice> * Map<int32,(int32*int32*int32)>) option