namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Shape
open Axis
open Split
open KDTree
open SceneTree
open BaseShape

module Construction =
    
    val mkTree : BaseShape[] -> KDTree
    val mkSceneTree : Shape[] -> SceneTree
    