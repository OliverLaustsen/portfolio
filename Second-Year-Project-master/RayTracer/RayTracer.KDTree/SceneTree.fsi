namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Split
open BaseShape


module SceneTree =
    open RayTracer.Shapes.Shape

    type SceneTree =
        | Node of bounds:BoundingBox * Split * depth:int * leftChild:SceneTree * rightChild:SceneTree
        | Leaf of bounds:BoundingBox * shapes:Shape[]

    val getBounds : SceneTree -> BoundingBox
    val getLeftChild : SceneTree -> SceneTree
    val getRightChild : SceneTree -> SceneTree
    val getSplit : SceneTree -> Split
    val getShapes : SceneTree -> Shape []