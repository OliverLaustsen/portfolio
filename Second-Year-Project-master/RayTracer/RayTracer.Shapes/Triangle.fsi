namespace RayTracer.Shapes
open RayTracer.Entities
open RayTracer.Shapes

module Triangle =
    open Shape
    open Point
    open Material
    open BoundingBox
    open Texture
    open BaseShape

    val mkTriangle : Point -> Point -> Point -> Texture -> bool -> Shape

    val mkPlyTriangle : Vertice -> Vertice -> Vertice -> bool -> BaseShape