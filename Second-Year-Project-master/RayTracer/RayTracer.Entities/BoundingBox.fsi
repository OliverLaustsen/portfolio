namespace RayTracer.Entities

open RayTracer.Entities.Point


module BoundingBox = 

    type BoundingBox =
        
        //Minimum of x,y,z values of the boundingbox
        abstract member lPoint : Point
        //Maximum of x,y,z values of the boundingbox
        abstract member hPoint : Point

    val getUnion : BoundingBox -> BoundingBox -> BoundingBox
    val getCornerPoints : BoundingBox -> Point list
    val makeBoundingBoxFromCorners : Point list -> BoundingBox
    val mkBoundingBox : Point -> Point -> BoundingBox
    val isInside : BoundingBox -> BoundingBox -> bool