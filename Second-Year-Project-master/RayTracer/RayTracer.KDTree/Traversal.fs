namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Shape
open Split
open Axis
open Helpers
open KDTree
open SceneTree
open Texture

module Traversal =
    
    let order (d:float) (leftnode) (rightnode) =
        if d > 0.0 then (leftnode, rightnode)
        else (rightnode, leftnode)
    
    let intersectGen (bounds:BoundingBox) (ray:Ray) getVCoordinate getPCoordinate = 
        let d = getVCoordinate (getVector ray)
        if d >= 0.0 then
            let t = (getPCoordinate bounds.lPoint - getPCoordinate (getPoint ray)) / d
            let t' = (getPCoordinate bounds.hPoint - getPCoordinate (getPoint ray)) / d
            (t,t')
        else
            let t = (getPCoordinate bounds.hPoint - getPCoordinate (getPoint ray)) / d
            let t' = (getPCoordinate bounds.lPoint - getPCoordinate (getPoint ray)) / d
            (t,t')

    //Intersection Methods
    let intersectX (bounds:BoundingBox) (ray:Ray) = intersectGen bounds ray Vector.getX Point.getX

    let intersectY (bounds:BoundingBox) (ray:Ray) = intersectGen bounds ray Vector.getY Point.getY

    let intersectZ (bounds:BoundingBox) (ray:Ray) = intersectGen bounds ray Vector.getZ Point.getZ

    let intersect (bounds:BoundingBox) (ray:Ray) =
        let (tx,tx') = intersectX bounds ray
        let (ty,ty') = intersectY bounds ray
        let (tz,tz') = intersectZ bounds ray

        let t = max tx (max ty tz)
        let t' = min tx' (min ty' tz')

        if t < t' && t' > 0.0 then 
            Some (t, t') 
        else 
            None

    let searchLeaf leaf ray texture t' =
            let hit = closestHit leaf ray texture 
            match hit with 
                Some(h) when Hit.getT h < t' -> Some(h)
                | _ -> None

    let searchSceneLeaf leaf ray t' =
            let hit = closestSceneHit leaf ray
            match hit with 
                Some(h) when Hit.getT h < t' -> Some(h)
                | _ -> None

    let searchNode node ray texture t t' (search : KDTree -> Ray -> Texture -> float -> float -> Hit option)  =
            let point = Ray.getPosition ray t
            let point2 = Ray.getPosition ray t'

            //let hit = mkEmptyHit
            let leftChild = KDTree.getLeftChild node
            let rightChild = KDTree.getRightChild node

            //Current split data.
            let split = (KDTree.getSplit node)
            let splitPoint = getSplitPoint split
            let axis = getAxis split

            let origin = getRayOriginOnAxis axis ray
            let direction = getRayDirectionOnAxis axis ray

            if direction = 0.0 then 
                if origin <= splitPoint then
                    search leftChild ray texture t t'
                else 
                    search rightChild ray texture t t'
            else
                let tHit = (splitPoint - origin) / direction
                let (fstNode, sndNode) = order direction leftChild rightChild
                if tHit <= t || tHit <= 0.0 then
                    search sndNode ray texture t t'
                else 
                    if tHit >= t' then
                        search fstNode ray texture t t'
                    else
                        match search fstNode ray texture t tHit with
                            | Some(h) -> Some(h)
                            | None -> search sndNode ray texture tHit t'

    let searchSceneNode node ray t t' (search : SceneTree -> Ray -> float -> float -> Hit option)  =
            let point = Ray.getPosition ray t
            let point2 = Ray.getPosition ray t'

            //let hit = mkEmptyHit
            let leftChild = SceneTree.getLeftChild node
            let rightChild = SceneTree.getRightChild node

            //Current split data.
            let split = (SceneTree.getSplit node)
            let splitPoint = getSplitPoint split
            let axis = getAxis split

            let origin = getRayOriginOnAxis axis ray
            let direction = getRayDirectionOnAxis axis ray

            if direction = 0.0 then 
                if origin <= splitPoint then
                    search leftChild ray t t'
                else 
                    search rightChild ray t t'
            else
                let tHit = (splitPoint - origin) / direction
                let (fstNode, sndNode) = order direction leftChild rightChild
                if tHit <= t || tHit <= 0.0 then
                    search sndNode ray t t'
                else 
                    if tHit >= t' then
                        search fstNode ray t t'
                    else
                        match search fstNode ray t tHit with
                            | Some(h) -> Some(h)
                            | None -> search sndNode ray tHit t'
    
    let rec search node ray texture (t:float) (t':float) =
        match node with
            | KDTree.Leaf (_,_) -> searchLeaf node ray texture t'
            | KDTree.Node (_,_,_,_,_) -> searchNode node ray texture t t' search

    let rec searchScene node ray (t:float) (t':float) =
        match node with
            | SceneTree.Leaf (_,_) -> searchSceneLeaf node ray t'
            | SceneTree.Node (_,_,_,_,_) -> searchSceneNode node ray t t' searchScene
    
    let traverse tree ray texture = 
        let intersection = intersect (KDTree.getBounds tree) ray
        match intersection with
            | Some(t,t') -> search tree ray texture t t'
            | None -> None

    let traverseScene tree ray = 
        let intersection = intersect (SceneTree.getBounds tree) ray
        match intersection with
            | Some(t,t') -> searchScene tree ray t t'
            | None -> None