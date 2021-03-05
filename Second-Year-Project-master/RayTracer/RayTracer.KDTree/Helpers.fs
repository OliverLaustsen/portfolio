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
open KDTree
open BaseShape
open System
open Bounded

module Helpers =
    
    (*General function for getting the middle of an axis*)
    let getMiddleGen getCoordinate (bounds:BoundingBox) = (abs(getCoordinate bounds.hPoint - getCoordinate bounds.lPoint))/2.0 + getCoordinate bounds.lPoint
    
    (*Wrapper for general function*)
    let getMiddle (bounds:BoundingBox) axis = 
       match axis with
         X -> getMiddleGen Point.getX bounds
       | Y -> getMiddleGen Point.getY bounds
       | Z -> getMiddleGen Point.getZ bounds

    (*Get the left node high point of a new boundingbox*)
    let getLeftHighpoint middle axis (bounds:BoundingBox) = 
        match axis with 
          X -> mkPoint middle (getY bounds.hPoint) (getZ bounds.hPoint)
        | Y -> mkPoint (getX bounds.hPoint) middle (getZ bounds.hPoint)
        | Z -> mkPoint (getX bounds.hPoint) (getY bounds.hPoint) middle

    (*Get the right node low point of a new boundingbox*)
    let getRightLowPoint middle axis (bounds:BoundingBox) = 
        match axis with 
          X -> mkPoint middle (getY bounds.lPoint) (getZ bounds.lPoint)
        | Y -> mkPoint (getX bounds.lPoint) middle (getZ bounds.lPoint)
        | Z -> mkPoint (getX bounds.lPoint) (getY bounds.lPoint) middle

    //General function for cutting a boundingbox into halves
    let cutGen (bounds:BoundingBox) middleOfAxis axis =  

        let leftHighPoint = getLeftHighpoint middleOfAxis axis bounds
        let rightLowPoint = getRightLowPoint middleOfAxis axis bounds

        let newleftBounds = mkBoundingBox bounds.lPoint leftHighPoint
        let newrightBounds = mkBoundingBox rightLowPoint bounds.hPoint

        (middleOfAxis, newleftBounds, newrightBounds)

    //Wrapper function for cutting boundingboxes on the middle
    let cutBoundingBox (axis:Axis) coordinate (bounds:BoundingBox) =
        match axis with
        | X -> cutGen bounds coordinate X
        | Y -> cutGen bounds coordinate Y
        | Z -> cutGen bounds coordinate Z

    //General sorting
    let getCoordinateOnBoundGen (point : Shape) getCoordinate getPoint = getCoordinate point

    let getCoordinateOnAxis (axis:Axis) = 
        match axis with     
              X -> Point.getX
            | Y -> Point.getY
            | Z -> Point.getZ 

    //Wrap exception
    let getIndexOf array predicate = 
        try
            Array.findIndex (predicate) array
        with 
            _ -> array |> Array.length
            
    let getCorrectedLIndex cutCoordinate getCoordinate (shapes:'a[]) (getBounds : 'a -> BoundingBox) = 
        getIndexOf shapes (fun shape -> (getCoordinate (getBounds shape).lPoint) >= cutCoordinate)

    let getShapesAfterCut index cutCoordinate getCoordinate (shapes:'a[]) (getBounds : 'a -> BoundingBox) = 
        let (left,right) = Array.splitAt index shapes
        let nCutIndex = getIndexOf right (fun (shape) -> cutCoordinate <= getCoordinate (getBounds shape).lPoint)
        nCutIndex 

    let cutOnCoordinate (axis:Axis) (cutCoordinate:float) (sortedByL : 'a[]) (sortedByH : 'a[]) getBounds = 
        let getCoordinate = getCoordinateOnAxis axis
        let lCutIndex = getCorrectedLIndex cutCoordinate getCoordinate sortedByL getBounds
        let hCutIndex = getIndexOf sortedByH (fun (s) -> cutCoordinate < getCoordinate (getBounds s).hPoint)
        let (left,_) = sortedByL |> Array.splitAt lCutIndex
        let (_,right) = sortedByH |> Array.splitAt hCutIndex
        (left,right)

    //Wrapper function for cutting boundingboxes on the median
    let cutMedian (axis:Axis) ((sortedByL,sortedByH):'a[]*'a[]) (getBounds : 'a -> BoundingBox) = 
        let getCoordinate = getCoordinateOnAxis axis
        let middleIndex = (Array.length sortedByL) / 2
        let cutCoordinate = getCoordinate (getBounds (sortedByL.[middleIndex])).lPoint
        
        let (left,right) = cutOnCoordinate axis cutCoordinate sortedByL sortedByH getBounds
        
        (left,right,(axis,cutCoordinate))



    //Computes the absolute minimum and maximum point based on the given shapes' points and creates a BoundingBox
    let getBoundsOfBounded shapes (getBounds: ('a -> BoundingBox)) =
        let initial = ((infinity,infinity,infinity),(-infinity,-infinity,-infinity))

        let foldFun ((minX,minY,minZ),(maxX,maxY,maxZ)) shape = 
            let bl = (shape |> getBounds).lPoint
            let bh = (shape |> getBounds).hPoint
            
            let nMinX = Math.Min(minX,(getX bl))      
            let nMinY = Math.Min(minY,(getY bl))
            let nMinZ = Math.Min(minZ,(getZ bl))
            
            let nMaxX = Math.Max(maxX,(getX bh))        
            let nMaxY = Math.Max(maxY,(getY bh))      
            let nMaxZ = Math.Max(maxZ,(getZ bh))    

            ((nMinX,nMinY,nMinZ),(nMaxX,nMaxY,nMaxZ))

        let ((mx,my,mz),(max,may,maz)) = Array.fold (fun (acc) shape -> (foldFun acc shape)) initial shapes
                                                                                                                

        let minP = mkPoint mx my mz
        let maxP = mkPoint max may maz    
        let e = 0.000001
        mkBoundingBox (minP--e) (maxP++e)
    
    //Places shapes on either left or right side of an split on some axis
    let placeByAxis leftShapes rightShapes shape (bounds:BoundingBox) (Split(coordinate,axis,_)) getCoordinate =
                let l = getCoordinate bounds.lPoint
                let h = getCoordinate bounds.hPoint
                if(h < coordinate) then (Array.append leftShapes [|shape|] ,rightShapes)
                else if (l > coordinate) then (leftShapes,Array.append rightShapes [|shape|])
                else (Array.append leftShapes [|shape|],Array.append rightShapes [|shape|])
    
    //Places shapes on either left or right side of an explicit axis
    let placeShape (leftShapes,rightShapes) shape (bounds:BoundingBox) split = 
        match Split.getAxis split with 
              X -> placeByAxis leftShapes rightShapes shape bounds split Point.getX
            | Y -> placeByAxis leftShapes rightShapes shape bounds split Point.getY
            | Z -> placeByAxis leftShapes rightShapes shape bounds split Point.getZ

    //Helper function for gettting the ray direction on an specific axis
    let getRayDirectionOnAxis axis ray =
        let direction = getVector ray
        match axis with
        | X -> Vector.getX direction
        | Y -> Vector.getY direction
        | Z -> Vector.getZ direction

    //Helper function for gettting the ray origin on an specific axis
    let getRayOriginOnAxis axis ray =
        let origin = getPoint ray
        match axis with
        | X -> getX origin
        | Y -> getY origin
        | Z -> getZ origin

    //Defines when a node should be a leaf
    //AKA when to stop making a kd-tree
    let isALeaf (depth:int) (shapes:'a[]) = shapes.Length = 0 || shapes.Length <= 30 || depth > 30

    let isASceneLeaf (depth:int) (shapes:'a[]) = shapes.Length = 0 || shapes.Length <= 10

    //Returns the closest hit on a leaf
    let closestHit leaf ray texture = 
        let mutable leafHit = mkEmptyHit
        let shapes = KDTree.getShapes leaf
        for shape in shapes do
            let hit = shape.Hit ray texture
            match hit with 
                Some(h) when mkEmptyHit = leafHit || (Hit.getT h) < Hit.getT leafHit -> leafHit <- h
                | _ -> leafHit <- leafHit

        if(leafHit = mkEmptyHit) then 
            None 
        else 
            Some(leafHit)

    //Returns the closest hit on a sceneleaf
    let closestSceneHit leaf ray = 
        let mutable leafHit = mkEmptyHit
        let shapes = SceneTree.getShapes leaf
        for shape in shapes do
            let hit = shape.Hit ray
            match hit with 
                Some(h) when mkEmptyHit = leafHit || (Hit.getT h) < Hit.getT leafHit -> 
                        let norm = Vector.normalise (Hit.getNormal h )
                        if(Vector.dotProduct norm (Ray.getVector ray)) > 0.0 then 
                            leafHit <- mkHit (Hit.getT h) -norm (Hit.getMaterial h)
                        else
                            leafHit <- mkHit (Hit.getT h) norm (Hit.getMaterial h)
                | _ -> leafHit <- leafHit

        if(leafHit = mkEmptyHit) then 
            None 
        else 
            Some(leafHit)
            