namespace RayTracer.Core

open Scene
open Camera
open RayTracer.Shapes.Shape
open RayTracer.Entities
open RayTracer.Entities.Vector
open RayTracer.Entities.Material
open RayTracer.Entities.Ray
open System.Drawing
open Hit


module Render = 

    val renderToFile : Scene -> Camera -> string -> unit
    val renderToScreen : Scene -> Camera -> unit
    val getHit : Scene -> Ray -> Hit option
