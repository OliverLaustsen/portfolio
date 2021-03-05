namespace RayTracer.Shapes
open RayTracer.Entities.Ray
open RayTracer.Entities
open RayTracer.Expressions
open Vector
open BoundingBox
open Point
open ExprToPoly
open Texture
open Bounded
open Hit
module BaseShape =
  
    type BaseShape =
        inherit Bounded
        abstract member Hit : (Ray -> Texture -> Hit option)
        abstract member Inside : (Point -> bool) option

    

                       