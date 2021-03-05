namespace RayTracer.Shapes
open RayTracer.Entities.Ray
open RayTracer.Entities
open RayTracer.Expressions
open Vector
open BoundingBox
open Texture
open Point
open ExprToPoly
open Hit
open Bounded
module BaseShape = 

    [<Interface>]
    type BaseShape =
        inherit Bounded
        abstract member Hit : (Ray -> Texture -> Hit option)
        abstract member Inside : (Point -> bool) option

     

