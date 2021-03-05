namespace RayTracer.TriangleParser
open FParsec
open RayTracer.Entities
open Point
open Vector
open Material
open RayTracer.Shapes.Shape
open Texture
open System.Collections.Generic
open RayTracer.Shapes.BaseShape

module Helpers =  
    
    //Exceptions
    exception VerticeException of string
    exception HeaderParserExpception of string
    exception TriangleException of string

    val str : (string -> Parser<string,'a>)
    val chr : (char -> Parser<char,'a>)
    val ws :  (Parser<unit,'a>)

    val calcNormal : Point-> Point -> Point -> Vector
    val prepareList : 'a list -> (int*'a) list
    val calcNormals : Map<int,Vertice> -> Dictionary<int,Vector.Vector> -> Map<int,Vertice>
    val makeTriangles : Map<int,Vertice> -> Map<int,int*int*int> -> bool -> BaseShape list
    val updateMap : (int*Vector) -> Dictionary<int,Vector> -> Dictionary<int,Vector>
    val prepareLists : Vertice list -> (int*int*int) list -> Map<int,Vertice> * Map<int,int*int*int> 