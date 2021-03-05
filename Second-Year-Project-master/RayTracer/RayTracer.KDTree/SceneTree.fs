namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Split
open Axis
open BaseShape


module SceneTree =
    open RayTracer.Shapes.Shape
    
    type SceneTree =
        | Node of bounds:BoundingBox * Split * depth:int * leftChild:SceneTree * rightChild:SceneTree
        | Leaf of bounds:BoundingBox * shapes:Shape[]

    let getLength node = 
        match node with
        | SceneTree.Leaf(_,shapes) -> shapes.Length
        | SceneTree.Node(_,_,_,_,_) -> -1
        
    let getLeftChild node = 
        match node with
        | SceneTree.Leaf(_,_) -> failwith "Is a leaf"
        | SceneTree.Node(_,_,_,leftChild,_) -> leftChild

    let getRightChild node = 
        match node with
        | SceneTree.Leaf(_,_) -> failwith "Is a leaf"
        | SceneTree.Node(_,_,_,_,rightChild) -> rightChild

    let rec getShapes node = 
        match node with
        | SceneTree.Leaf(_,shapes) -> shapes
        | _ -> failwith "Not a leaf"

    let getBounds node = 
        match node with
        | SceneTree.Leaf(bounds,_) -> bounds
        | SceneTree.Node(bounds,_,_,_,_) -> bounds

    let getDepth node =
        match node with
        | SceneTree.Leaf(_,_) -> -1
        | SceneTree.Node(_,_,depth,_,_) -> depth

    let getSplit node =
        match node with
        | SceneTree.Node(_,split,_,_,_) -> split
        | _ -> failwith "Not a node"