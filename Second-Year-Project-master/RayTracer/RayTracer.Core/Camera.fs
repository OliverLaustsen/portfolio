namespace RayTracer.Core
open RayTracer.Entities
module Camera = 


    type Camera = 
     | Camera of Point.Point * Point.Point * Vector.Vector * float * float * float * int * int

    let mkCamera position lookAt upVector zoom unitX unitY resWidth resHeight = Camera(position,lookAt,upVector,zoom,unitX,unitY,resWidth,resHeight)
    let getPos (Camera(position,_,_,_,_,_,_,_)) = position
    let getLookAt (Camera(_,lookAt,_,_,_,_,_,_)) = lookAt
    let getUpVector (Camera(_,_,upVector,_,_,_,_,_)) = upVector
    let getZoom (Camera(_,_,_,zoom,_,_,_,_)) = zoom
    let getUnitX (Camera(_,_,_,_,unitX,_,_,_)) = unitX
    let getUnitY (Camera(_,_,_,_,_,unitY,_,_)) = unitY
    let getResWidth (Camera(_,_,_,_,_,_,resWidth,_)) = resWidth
    let getResHeight (Camera(_,_,_,_,_,_,_,resHeight)) = resHeight
