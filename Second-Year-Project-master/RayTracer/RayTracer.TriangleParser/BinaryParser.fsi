namespace RayTracer.TriangleParser
open FParsec
open RayTracer.TriangleParser.Helpers
open RayTracer.Shapes.Shape
open RayTracer.TriangleParser.HeaderParser
open RayTracer
open System.IO
open System 


module BinaryParser =  
    val skip : Stream -> int -> unit
    val parseBinary : (int*Header) -> string -> (Map<int,Vertice> * Map<int,int*int*int>) option