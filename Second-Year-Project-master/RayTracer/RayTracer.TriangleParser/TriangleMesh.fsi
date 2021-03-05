namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.Shapes.BaseShape
open Point
open Colour
open Material
open BoundingBox
open Texture

module TriangleMesh =

    val mkTriangleMesh : string -> bool -> BaseShape
