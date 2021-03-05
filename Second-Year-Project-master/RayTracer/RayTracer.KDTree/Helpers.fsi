namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Shape
open Texture
open Axis
open Split
open KDTree
open SceneTree
open BaseShape
open Bounded

module Helpers =
    val getMiddleGen : (Point -> float) -> BoundingBox -> float
    val getMiddle : BoundingBox -> Axis -> float
    val cutBoundingBox : Axis-> float -> BoundingBox -> float * BoundingBox * BoundingBox
    val cutOnCoordinate : Axis -> float -> 'a[] -> 'a[] -> ('a -> BoundingBox) -> ('a[]*'a[])
    val getCoordinateOnAxis : Axis -> (Point -> float)
    val cutMedian : Axis -> 'a[]*'a[] -> ('a -> BoundingBox) -> ('a[]*'a[]*(Axis*float))
    val isALeaf : int -> 'a [] -> bool
    val isASceneLeaf : int -> 'a [] -> bool
    val getBoundsOfBounded : 'a[] -> ('a -> BoundingBox) -> BoundingBox
    val getRayDirectionOnAxis : Axis -> Ray -> float
    val getRayOriginOnAxis : Axis -> Ray -> float
    val closestHit : KDTree -> Ray -> Texture -> Hit option
    val closestSceneHit : SceneTree -> Ray -> Hit option