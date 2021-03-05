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
open Texture

module Traversal =
    
    val traverse : KDTree -> Ray -> Texture -> Hit option
    val traverseScene : SceneTree -> Ray -> Hit option