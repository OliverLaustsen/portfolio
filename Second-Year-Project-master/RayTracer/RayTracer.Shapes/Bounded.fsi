namespace RayTracer.Shapes
open RayTracer.Entities.Ray
open RayTracer.Entities
open RayTracer.Expressions
open Vector
open BoundingBox
open Point
open ExprToPoly
open Texture
open Hit
module Bounded =

    [<Interface>]
    type Bounded = 
        abstract member BoundingBox : BoundingBox
        abstract member TransformedBoundingBox : BoundingBox


    

                       