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


module KDTree =
    open RayTracer.Shapes.Shape
    
    type KDTree =
        | Node of bounds:BoundingBox * Split * depth:int * leftChild:KDTree * rightChild:KDTree
        | Leaf of bounds:BoundingBox * shapes:BaseShape []

    let getLength node = 
        match node with
        | KDTree.Leaf(_,shapes) -> shapes.Length
        | KDTree.Node(_,_,_,_,_) -> -1
        
    let getLeftChild node = 
        match node with
        | KDTree.Leaf(_,_) -> failwith "Is a leaf"
        | KDTree.Node(_,_,_,leftChild,_) -> leftChild

    let getRightChild node = 
        match node with
        | KDTree.Leaf(_,_) -> failwith "Is a leaf"
        | KDTree.Node(_,_,_,_,rightChild) -> rightChild

    let rec getShapes node = 
        match node with
        | KDTree.Leaf(_,shapes) -> shapes
        | _ -> failwith "Not a leaf"

    let getBounds node = 
        match node with
        | KDTree.Leaf(bounds,_) -> bounds
        | KDTree.Node(bounds,_,_,_,_) -> bounds

    let getDepth node =
        match node with
        | KDTree.Leaf(_,_) -> -1
        | KDTree.Node(_,_,depth,_,_) -> depth

    let getSplit node =
        match node with
        | KDTree.Node(_,split,_,_,_) -> split
        | _ -> failwith "Not a node"