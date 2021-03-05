namespace RayTracer.TriangleParser
open FParsec
open RayTracer.Entities
open RayTracer.Shapes.Shape
open RayTracer.TriangleParser.HeaderParser

module TxtParser =  
    val parseTxt : Header -> string -> (Map<int,Vertice> * Map<int,int*int*int>) option