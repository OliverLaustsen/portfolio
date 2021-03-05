namespace RayTracer.Entities
open RayTracer.Entities.Ray
open RayTracer.Entities.Vector
open RayTracer.Entities.Point
open System.Threading.Tasks
open BoundingBox

module Transformation =


    type Transformation = 
        
        abstract member Matrix : float[,]

    let mkTransformation m =
        { new Transformation
          with member this.Matrix = m
        }

    let transformPoint (p:Point) (matrix:float[,]) =
        let px = Point.getX p
        let py = Point.getY p
        let pz = Point.getZ p
        mkPoint (px*matrix.[0,0] + py*matrix.[0,1] + pz*matrix.[0,2] + matrix.[0,3])
                (px*matrix.[1,0] + py*matrix.[1,1] + pz*matrix.[1,2] + matrix.[1,3])
                (px*matrix.[2,0] + py*matrix.[2,1] + pz*matrix.[2,2] + matrix.[2,3])
    
    let transformPoints (points:Point list) (t:Transformation) = 
        let mutable transformedPoints = List.Empty
        for point in points do
            let transformedPoint = transformPoint point t.Matrix
            transformedPoints <- transformedPoint::transformedPoints
        transformedPoints

    let transformBoundingbox (boundingBox : BoundingBox) (t:Transformation) = 
        let cornerPoints = getCornerPoints boundingBox
        let transformedCornerPoints = transformPoints cornerPoints t
        makeBoundingBoxFromCorners transformedCornerPoints
    
    let transformVector v (matrix:float[,]) =
        let vx = Vector.getX v
        let vy = Vector.getY v
        let vz = Vector.getZ v
        mkVector (vx*matrix.[0,0] + vy*matrix.[0,1] + vz*matrix.[0,2] + 0.0*matrix.[0,3])
                 (vx*matrix.[1,0] + vy*matrix.[1,1] + vz*matrix.[1,2] + 0.0*matrix.[1,3])
                 (vx*matrix.[2,0] + vy*matrix.[2,1] + vz*matrix.[2,2] + 0.0*matrix.[2,3])


    let translate x y z =
        let translate = mkTransformation (array2D [|[|1.0;0.0;0.0;x|];[|0.0;1.0;0.0;y|];[|0.0;0.0;1.0;z|];[|0.0;0.0;0.0;1.0|]|])
        let inverseTranslate = mkTransformation (array2D [|[|1.0;0.0;0.0;-x|];[|0.0;1.0;0.0;-y|];[|0.0;0.0;1.0;-z|];[|0.0;0.0;0.0;1.0|]|])
        (translate,inverseTranslate)

    let mirrorX = 
        let mirrorX = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;-1.0;0.0;0.0|];[|0.0;0.0;-1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseMirrorX = mkTransformation (array2D [|[|-1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (mirrorX,inverseMirrorX)

    let mirrorY = 
        let mirrorY = mkTransformation (array2D [|[|-1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;-1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseMirrorY = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;-1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (mirrorY,inverseMirrorY)

    let mirrorZ = 
        let mirrorZ = mkTransformation (array2D [|[|-1.0;0.0;0.0;0.0|];[|0.0;-1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseMirrorZ = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;-1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (mirrorZ,inverseMirrorZ)

    let scale x y z =
        let scale = mkTransformation (array2D [|[|x;0.0;0.0;0.0|];[|0.0;y;0.0;0.0|];[|0.0;0.0;z;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseScale = mkTransformation (array2D [|[|1.0/x;0.0;0.0;0.0|];[|0.0;1.0/y;0.0;0.0|];[|0.0;0.0;1.0/z;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (scale,inverseScale)
    
    let shearxy f =
        let shear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|f;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseShear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|-f;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (shear,inverseShear)
            
    let shearxz f =
        let shear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|f;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseShear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|-f;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (shear,inverseShear)
            
    let shearyx f =
        let shear = mkTransformation (array2D [|[|1.0;f;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseShear = mkTransformation (array2D [|[|1.0;-f;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (shear,inverseShear)
            
    let shearyz f =
        let shear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;f;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseShear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;-f;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (shear,inverseShear)
            
    let shearzx f =
        let shear = mkTransformation (array2D [|[|1.0;0.0;f;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseShear = mkTransformation (array2D [|[|1.0;0.0;-f;0.0|];[|0.0;1.0;0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (shear,inverseShear)
            
    let shearzy f =
        let shear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;f;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseShear = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;1.0;-f;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (shear,inverseShear)

    let rotateX r =
        let rotateX = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;cos(r);-sin(r);0.0|];[|0.0;sin(r);cos(r);0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseRotateX = mkTransformation (array2D [|[|1.0;0.0;0.0;0.0|];[|0.0;cos(r);sin(r);0.0|];[|0.0;-sin(r);cos(r);0.0|];[|0.0;0.0;0.0;1.0|]|])
        (rotateX,inverseRotateX)

    let rotateY r =
        let rotateY = mkTransformation (array2D [|[|cos(r);0.0;sin(r);0.0|];[|0.0;1.0;0.0;0.0|];[|-sin(r);0.0;cos(r);0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseRotateY = mkTransformation (array2D [|[|cos(r);0.0;-sin(r);0.0|];[|0.0;1.0;0.0;0.0|];[|sin(r);0.0;cos(r);0.0|];[|0.0;0.0;0.0;1.0|]|])
        (rotateY,inverseRotateY)

    let rotateZ r =
        let rotateZ = mkTransformation (array2D [|[|cos(r);-sin(r);0.0;0.0|];[|sin(r);cos(r);0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        let inverseRotateZ = mkTransformation (array2D [|[|cos(r);sin(r);0.0;0.0|];[|-sin(r);cos(r);0.0;0.0|];[|0.0;0.0;1.0;0.0|];[|0.0;0.0;0.0;1.0|]|])
        (rotateZ,inverseRotateZ)

    let mergeTransform (t1:Transformation) (t2:Transformation) =
        let m = t1.Matrix
        let n = t2.Matrix
        let x = Array2D.length1 m
        let y = Array2D.length2 n
        let z = Array2D.length2 m
                    
        mkTransformation (let result = Array2D.create x y  0.0
                          Parallel.For(0, x, (fun i->
                            for j = 0 to y - 1 do
                                for k = 0 to z - 1 do
                                    result.[i,j] <- result.[i,j] + m.[i,k] * n.[k,j]))  
                            |> ignore
                          result)

    let rec mergeTransformList (t:Transformation list) =
        match t with
        |s::se::ss -> let ts = mergeTransform s se
                      let tt = ts::ss
                      mergeTransformList tt
        |s::ss -> match ss with
                  |[]-> s
                  |_ -> failwith "Should not hit this point"
        |[] -> failwith "Recieved empty list"

    let mergeInverseMatrix (t:Transformation list) =
        let tt = List.rev t
        let ts = mergeTransformList tt
        ts

    let mergeTransformations list = 
        let mutable transform = []
        let mutable inverse = []
        for (t,i) in list do
            transform <- t::transform
            inverse <- i::inverse
        (mergeTransformList transform,mergeInverseMatrix inverse)