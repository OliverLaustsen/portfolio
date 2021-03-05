namespace RayTracer.KDTree
open RayTracer.Entities
open RayTracer.Shapes
open BoundingBox
open Point
open Ray
open Hit
open Shape
open KDTree
open Axis
open Helpers
open Split
open System
open BaseShape

module Construction =
    let mkLeaf bounds shapes = KDTree.Leaf(bounds, shapes)     
    let mkSceneLeaf bounds shapes = SceneTree.Leaf( bounds, shapes)         
    let getDifference ((left,right,_): ('a[]*'a[]*'b)) = abs (left.Length - right.Length)
    let getMostBalanced cuts = List.sortBy getDifference cuts |> List.head

    let getBestCut (sorted: Collections.Generic.IDictionary<Axis,('a[]*'a[])>) getBounds = 
       let xCut = cutMedian X sorted.[X] getBounds
       let yCut = cutMedian Y sorted.[Y] getBounds
       let zCut = cutMedian Z sorted.[Z] getBounds
       getMostBalanced [xCut;yCut;zCut]

    let cutEmptySpaceOnAxis ((sortedByL,sortedByH):'a[]*'a[]) (getBounds:('a -> BoundingBox)) (bounds:BoundingBox) (axis:Axis) = 
        let getCoordinate = getCoordinateOnAxis axis        
        let lowestCoord = getCoordinate (getBounds (sortedByL.[0])).lPoint
        let highestCoord = getCoordinate ((getBounds (Array.last sortedByH))).hPoint


        let ldis = abs lowestCoord - abs (getCoordinate bounds.lPoint)
        let hdis = (getCoordinate bounds.hPoint) - highestCoord
        let length = getCoordinate bounds.hPoint - getCoordinate bounds.lPoint

        if(ldis > hdis) then 
            let percentage = (ldis/length)
            if(percentage > 0.1) then 
                Some(axis,lowestCoord,ldis,sortedByL,sortedByH)
            else 
                None
        else
            let percentage = (hdis/length)
            if(percentage > 0.1) then 
                Some(axis,highestCoord,hdis,sortedByL,sortedByH)
            else 
                None

    let cutEmptySpace (sorted: Collections.Generic.IDictionary<Axis,('a[]*'a[])>) (getBounds) (bounds:BoundingBox) =
        let xcut = cutEmptySpaceOnAxis sorted.[X] getBounds bounds X
        let ycut = cutEmptySpaceOnAxis sorted.[Y] getBounds bounds Y
        let zcut = cutEmptySpaceOnAxis sorted.[Z] getBounds bounds Z
        let mutable cuts = List.empty
        
        if(xcut.IsNone && ycut.IsNone && zcut.IsNone) then None
        else
            match xcut with 
                Some(c) -> cuts <- c::cuts
                | _ -> ()

            match ycut with 
                Some(c) -> cuts <- c::cuts
                | _ -> ()

            match zcut with 
                Some(c) -> cuts <- c::cuts
                | _ -> ()

            let sorted =  List.sortBy (fun (_,_,p,_,_) -> p) cuts
            let last = List.last sorted


            Some(last)

    let rec mkNodes bounds (axis:Axis) depth shapes failcount = 
        let getBaseShapeB = (fun (s:BaseShape) -> s.BoundingBox )

        let sortedByLX = Array.sortBy (fun (s : BaseShape) -> getX s.TransformedBoundingBox.lPoint) shapes
        let sortedByHX = Array.sortBy (fun (s : BaseShape) -> getX s.TransformedBoundingBox.hPoint) shapes
        let sortedByLY = Array.sortBy (fun (s : BaseShape) -> getY s.TransformedBoundingBox.lPoint) shapes
        let sortedByHY = Array.sortBy (fun (s : BaseShape) -> getY s.TransformedBoundingBox.hPoint) shapes
        let sortedByLZ = Array.sortBy (fun (s : BaseShape) -> getZ s.TransformedBoundingBox.lPoint) shapes
        let sortedByHZ = Array.sortBy (fun (s : BaseShape) -> getZ s.TransformedBoundingBox.hPoint) shapes

        let sortedMap = dict[X, (sortedByLX,sortedByHX); Y, (sortedByLY,sortedByHY);Z,(sortedByLZ,sortedByHZ)]


        if isALeaf depth shapes
        then
            mkLeaf bounds shapes
        else
            match cutEmptySpace sortedMap getBaseShapeB bounds with
                 Some(a,coor,_,lSorted,rSorted) -> 
                           let (middle,b1,b2) = cutBoundingBox a coor bounds
                           let getCoordinate = getCoordinateOnAxis a

                           let (left,right) = cutOnCoordinate a coor lSorted rSorted getBaseShapeB
                           if not (left.Length = 0 || right.Length = 0) then failwith "nothing is zero"
                           let newDepth = depth + 1
                           let newAxis = getNextAxis axis

                           Node(bounds, Split(coor,a,(left.Length,right.Length)), depth, mkNodes b1 newAxis newDepth left 0, mkNodes b2 newAxis newDepth right 0)
                | None ->  
                        let (left,right,(a,c)) = getBestCut sortedMap getBaseShapeB
                        let (_,b1,b2) = cutBoundingBox a c bounds
                        
                        let nfailcount = 
                            if (shapes.Length = left.Length) && (shapes.Length = right.Length) then 
                                failcount+1 
                            else 
                                0

                        if(nfailcount > 2) then mkLeaf bounds shapes
                        else              
                            let newDepth = depth + 1
                            let newAxis = getNextAxis axis 
                            Node(bounds, Split(c,a,(left.Length,right.Length)), depth, mkNodes b1 newAxis newDepth left nfailcount, mkNodes b2 newAxis newDepth right nfailcount)

    let rec mkSceneNodes bounds (axis:Axis) depth shapes failcount = 
        let getBaseShapeB = (fun (s:Shape) -> s.TransformedBoundingBox )

        if isASceneLeaf depth shapes
        then
            mkSceneLeaf bounds shapes
        else 
            let sortedByLX = Array.sortBy (fun (s : Shape) -> getX s.TransformedBoundingBox.lPoint) shapes
            let sortedByHX = Array.sortBy (fun (s : Shape) -> getX s.TransformedBoundingBox.hPoint) shapes
            let sortedByLY = Array.sortBy (fun (s : Shape) -> getY s.TransformedBoundingBox.lPoint) shapes
            let sortedByHY = Array.sortBy (fun (s : Shape) -> getY s.TransformedBoundingBox.hPoint) shapes
            let sortedByLZ = Array.sortBy (fun (s : Shape) -> getZ s.TransformedBoundingBox.lPoint) shapes
            let sortedByHZ = Array.sortBy (fun (s : Shape) -> getZ s.TransformedBoundingBox.hPoint) shapes

            let sortedMap = dict[X, (sortedByLX,sortedByHX); Y, (sortedByLY,sortedByHY);Z,(sortedByLZ,sortedByHZ)]

            match cutEmptySpace sortedMap getBaseShapeB bounds with
                 Some(a,coor,_,lSorted,rSorted) -> 
                           let (middle,b1,b2) = cutBoundingBox a coor bounds
                           let getCoordinate = getCoordinateOnAxis a

                           let (left,right) = cutOnCoordinate a coor lSorted rSorted getBaseShapeB
                           if not (left.Length = 0 || right.Length = 0) then failwith "nothing is zero"
                           let newDepth = depth + 1
                           let newAxis = getNextAxis axis

                           SceneTree.Node(bounds, Split(coor,a,(left.Length,right.Length)), depth, mkSceneNodes b1 newAxis newDepth left 0, mkSceneNodes b2 newAxis newDepth right 0)
                | None ->  
                        let (left,right,(a,c)) = getBestCut sortedMap getBaseShapeB
                        let (_,b1,b2) = cutBoundingBox a c bounds
                        
                        let nfailcount = 
                            if (shapes.Length = left.Length) && (shapes.Length = right.Length) then 
                                failcount+1 
                            else 
                                0

                        if(nfailcount > 2) then mkSceneLeaf bounds shapes
                        else              
                            let newDepth = depth + 1
                            let newAxis = getNextAxis axis 
                            SceneTree.Node(bounds, Split(c,a,(left.Length,right.Length)), depth, mkSceneNodes b1 newAxis newDepth left nfailcount, mkSceneNodes b2 newAxis newDepth right nfailcount)


    let mkTree (shapes:BaseShape[]) = 
        let bounds = getBoundsOfBounded shapes (fun (s:BaseShape) -> s.BoundingBox)   
        let nodes = mkNodes bounds X 0 shapes 0
        nodes

    let mkSceneTree shapes = 
        let bounds = getBoundsOfBounded shapes (fun (s:Shape) -> s.TransformedBoundingBox)    
        let nodes = mkSceneNodes bounds X 0 shapes 0
        nodes
           
           
    