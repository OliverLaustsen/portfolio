namespace RayTracer.Shapes
open RayTracer.Expressions.ExprParse
open RayTracer.Expressions.ExprToPoly
open RayTracer.Entities.Ray
open RayTracer.Entities
open Vector
open BoundingBox
open BaseShape

module Implicit = 

    val mkImplicit : string -> BaseShape

