namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Split
open BaseShape


module KDTree =
    open RayTracer.Shapes.Shape

    type KDTree =
        | Node of bounds:BoundingBox * Split * depth:int * leftChild:KDTree * rightChild:KDTree
        | Leaf of bounds:BoundingBox * shapes:BaseShape []

    val getBounds : KDTree -> BoundingBox
    val getLeftChild : KDTree -> KDTree
    val getRightChild : KDTree -> KDTree
    val getSplit : KDTree -> Split
    val getShapes : KDTree -> BaseShape[]